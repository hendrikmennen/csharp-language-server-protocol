# AGENTS.md

Guidance for AI agents working in this repository.

This is **OneWare's fork** of [OmniSharp/csharp-language-server-protocol](https://github.com/OmniSharp/csharp-language-server-protocol).
It exists to ship the library on .NET 10 as `OneWare.*` NuGet packages and to fix protocol bugs
that block [OneWare Studio](https://github.com/one-ware/OneWare) faster than upstream does.

---

## Fork invariants — do not break these

1. **Assembly names, namespaces and public API stay identical to upstream.** Only the NuGet
   *package id* is prefixed with `OneWare.`. `OneWare.OmniSharp.Extensions.LanguageClient`
   still contains `OmniSharp.Extensions.LanguageClient.dll`. This is what makes the packages a
   drop-in replacement.
2. **The strong-name key `lsp.snk` is committed and must not change.** Public key token
   `6d868dff454e6022`, the same as the official packages.
3. **All fork-specific packaging lives in [src/OneWare.Packaging.targets](src/OneWare.Packaging.targets)**,
   imported from [src/Directory.Build.targets](src/Directory.Build.targets). Keep it that way so
   merging upstream stays conflict-free. Do not scatter fork changes across `.csproj` files.
4. **Prefer fixing things in the library over working around them in OneWare.** Most converters
   are attached with `[JsonConverter(typeof(X))]` *on the type*, and attribute converters win over
   anything registered via `LanguageClientOptions.Serializer` / `LspSerializer.AddOrReplaceConverters`.
   A consumer therefore cannot override them — the fix has to land here.
5. **Keep the diff against upstream small.** Every extra change is a future merge conflict.

---

## Environment

| | |
|---|---|
| SDK | .NET 10 (`global.json` pins the `10.0.100` band, `rollForward: latestMinor`) |
| Target frameworks | `netstandard2.0;netstandard2.1;net8.0;net10.0` (libs), `net10.0` (tests) |
| Serializer | Newtonsoft.Json |
| Package versions | Central Package Management — [Directory.Packages.props](Directory.Packages.props) |
| Solution | `LSP.sln` |

### Commands

```bash
dotnet build LSP.sln -c Release
dotnet test  LSP.sln -c Release
dotnet test test/Lsp.Tests/Lsp.Tests.csproj -c Release --filter "FullyQualifiedName~SomeTests"
```

Baseline: **all tests pass**, no exceptions. If something fails on a clean tree, that is a real
problem — investigate before doing anything else.

---

## Workflow A — a bug report arrives from OneWare

Reports usually name a type, a JSON payload and the wrong result. Work through it in this order.

### 1. Reproduce before believing

Never assume the report is still accurate; upstream may have fixed it since the version OneWare
consumes. Equally, never assume it *is* fixed because the code "looks right".

Fastest reproduction is a scratch console app outside the repo:

```bash
mkdir -p /tmp/repro && cd /tmp/repro
```

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="/path/to/csharp-language-server-protocol/src/Protocol/Protocol.csproj" />
  </ItemGroup>
</Project>
```

```csharp
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities; // ClientVersion
using OmniSharp.Extensions.LanguageServer.Protocol.Models;             // most protocol types
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;      // LspSerializer

var s = new LspSerializer(ClientVersion.Lsp3);
var hover = s.DeserializeObject<Hover>("""{"contents":{"language":"vhdl","value":"x"}}""");
Console.WriteLine(hover.Contents.HasMarkedStrings);
```

Report back honestly which issues reproduce and which do not. Some reported issues turn out to be
consumer-side and need no library change — say so rather than inventing a fix.

### 2. Locate the cause

Union types (`XOrY`) are where the bugs live. Their converters sit in
[src/Protocol/Serialization/Converters](src/Protocol/Serialization/Converters) and the models in
`src/Protocol/Models`. Protocol features are one file per feature under
`src/Protocol/Features/{Document,Workspace,Window,General,Client,Server}`.

The recurring failure mode: **a union discriminated on the wrong thing.** Two LSP types often share
a `value` property and differ only by a sibling key, so the converter must test the *discriminating
key's presence*, not a value or a type. Both bugs fixed so far were exactly this.

### 3. Fix it

- Keep the change minimal and local to the converter.
- Preserve the existing behaviour for genuinely ambiguous input; note the choice in a comment.
- Add a `JsonToken.Null` guard if the converter calls `JObject.Load` unguarded.
- Explain *why* in a comment, referencing the spec shape. The next reader needs to know why the
  obvious-looking check is wrong.

### 4. Add regression tests — always

Both bugs survived for years because the types had **zero tests**. Put them in
`test/Lsp.Tests/Models/<Type>Tests.cs`, following
[TextEditOrInsertReplaceEditTests.cs](test/Lsp.Tests/Models/TextEditOrInsertReplaceEditTests.cs) and
[MarkedStringsOrMarkupContentTests.cs](test/Lsp.Tests/Models/MarkedStringsOrMarkupContentTests.cs):

```csharp
private static readonly LspSerializer Serializer = new(ClientVersion.Lsp3);
```

Cover every arm of the union, the ambiguous case, null, and a round-trip. Include the reporter's
exact payload as its own test — verify the round-trip expectation by running it rather than
predicting the output.

### 5. Verify and release

```bash
dotnet test LSP.sln -c Release          # must be fully green
git checkout -- test/coverage           # see gotchas
```

Then follow **Releasing** below. Bug fixes are patch releases.

### 6. Report back

Per issue: does it reproduce, what caused it, what changed, what tests were added, and **which
workarounds OneWare can now delete**. That last part is the most useful output.

---

## Workflow B — updating to a newer LSP specification

The spec version last caught up with is recorded in
[language-server-protocol.sha.txt](language-server-protocol.sha.txt) (and
[debug-adapter-protocol.sha.txt](debug-adapter-protocol.sha.txt) for DAP). These are plain markers;
nothing reads them at build time. Update them when you finish a spec pass.

1. **Diff the spec.** Compare the recorded `lastSha` against the current
   `microsoft/language-server-protocol` `gh-pages` branch, or read the metaModel. Produce a concrete
   list of added/changed requests, notifications, models and capabilities before writing code.
2. **Audit before implementing.** Much may already exist — the repository currently covers LSP 3.17
   in full plus the 3.18 additions. Grep `src/Protocol/Features` for each new method name first.
3. **Follow the existing feature-file pattern.** One file per feature, containing the params, the
   result models, the client capability and the registration options, wired up with the JsonRpc
   source generator attributes. Copy the closest existing feature; do not invent a new shape.
   See [docs/lsp.md](docs/lsp.md), [docs/source-generation.md](docs/source-generation.md) and
   [architecture.md](architecture.md).
4. **Let the generators do the work.** Handler interfaces, extension methods and registration
   plumbing are generated by `src/JsonRpc.Generators`. Do not hand-write what the generator emits.
5. **Proposed features go in `Protocol.Proposals`**, not `Protocol`.
6. **Run `dotnet test LSP.sln`.** `Lsp.Tests.FoundationTests` enforces the conventions every feature
   must satisfy and will catch most mistakes. `Generation.Tests` snapshots the generator output —
   review any snapshot change, then delete `test/Generation.Tests/snapshots/*.received.cs` and re-run
   to accept it, but only once you are sure the change is intended.
7. New spec surface is a **minor** version bump.

---

## Releasing

[.github/workflows/release.yml](.github/workflows/release.yml) builds, tests, packs and pushes.
It is deliberately self-contained: it does not use NUKE or GitVersion.

```bash
git tag -a v1.2.3 -m "..." && git push origin v1.2.3
```

- Tag pushes publish to nuget.org automatically and create a GitHub release.
- `workflow_dispatch` allows a dry run — supply `version` and leave `publish` unchecked.
- The version flows in as `-p:OneWarePackageVersion=<v>`; without it, the normal GitVersion path applies.
- Requires the `NUGET_API_SECRET` repository secret. It is already configured.
- **Confirm the version with the user before tagging.** Tags trigger an irreversible publish;
  a package version can never be re-uploaded to nuget.org.

Six packages ship, and the whole closure must be published together or NuGet silently resolves the
original OmniSharp packages instead:

```
src/JsonRpc.Generators/JsonRpc.Generators.csproj
src/JsonRpc/JsonRpc.csproj
src/Protocol/Protocol.csproj
src/Shared/Shared.csproj
src/Client/Client.csproj
src/Server/Server.csproj
```

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) only builds and tests; its upstream publish
job was removed because it depended on secrets that do not exist in this fork.

---

## Gotchas

**Source generators must reference the *lowest* supported Roslyn, never the newest.** Generators are
loaded by the compiler that is running, so referencing a newer `Microsoft.CodeAnalysis` than `csc`
raises `CS9057`, silently skips the generator, and cascades into hundreds of confusing `CS0246` /
`CS0426` errors. If you see that flood, suspect the Roslyn pin in `Directory.Packages.props`, not the
source. `Microsoft.CodeAnalysis.Analyzers` is analyzer-authoring rules only and does not affect this.

**`test/coverage/*.json` are git-tracked and rewritten by every test run.** Always
`git checkout -- test/coverage` before committing, or they end up in the diff.

**Disable bash history expansion in its own command.** `set +H && grep "Passed!"` still fails with
`bash: !: event not found`, because expansion happens when the whole line is parsed. Run `set +H`
first, as a separate command.

**Grep build output, do not read it.** `dotnet build/test` output is dominated by `NU1902`, `CS8602`
and `RS1038` warnings. Redirect to a file and filter:

```bash
dotnet test LSP.sln -c Release --nologo > /tmp/test.txt 2>&1
grep -aE "Passed!|Failed!|\[FAIL\]|error CS" /tmp/test.txt | sort -u
```

**`Directory.Build.props` is evaluated before the project body.** Anything depending on
`AssemblyName` — such as `PackageId` — must go in `Directory.Build.targets`.

**Packing outside NUKE needs an icon fallback.** Upstream expects NUKE to download
`.nuke/temp/packageicon.png`; `OneWare.Packaging.targets` handles the absence to avoid `NU5046`.
Drop a `build/packageicon.png` to give the packages a OneWare logo.

---

## Fixed so far

| Symptom | Cause |
|---|---|
| `InsertReplaceEdit` in a completion item became a `TextEdit` with a null `Range` | `TextEditOrInsertReplaceEditConverter` tested whether `insert` was a JSON *string*, but it is a `Range` object, so the branch was unreachable |
| Scalar `MarkedString` in `Hover.contents` became `MarkupContent`, dropping `language` | `MarkedStringsOrMarkupContentConverter` treated every non-array object as `MarkupContent`; must discriminate on `language` vs `kind` |
