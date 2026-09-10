using FluentAssertions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using Xunit;

namespace Lsp.Tests.Models
{
    public class MarkedStringsOrMarkupContentTests
    {
        private static readonly LspSerializer Serializer = new(ClientVersion.Lsp3);

        [Fact]
        public void Should_Deserialize_Scalar_MarkedString()
        {
            // `Hover.contents` allows a single MarkedString object, which servers such as vhdl_ls send.
            var hover = Serializer.DeserializeObject<Hover>(
                """{"contents":{"language":"vhdl","value":"signal clk : std_logic"}}"""
            );

            hover.Contents.HasMarkedStrings.Should().BeTrue();
            hover.Contents.HasMarkupContent.Should().BeFalse();
            hover.Contents.MarkedStrings.Should().ContainSingle()
                 .Which.Should().BeEquivalentTo(new MarkedString("vhdl", "signal clk : std_logic"));
        }

        [Fact]
        public void Should_Deserialize_Scalar_MarkedString_Without_Language()
        {
            var contents = Serializer.DeserializeObject<MarkedStringsOrMarkupContent>("""{"value":"abc"}""");

            // Ambiguous: no `language` and no `kind`. MarkupContent stays the fallback for compatibility.
            contents.HasMarkupContent.Should().BeTrue();
            contents.MarkupContent!.Value.Should().Be("abc");
        }

        [Fact]
        public void Should_Deserialize_MarkedString_Array()
        {
            var hover = Serializer.DeserializeObject<Hover>(
                """{"contents":[{"language":"vhdl","value":"signal clk : std_logic"},"plain"]}"""
            );

            hover.Contents.HasMarkedStrings.Should().BeTrue();
            hover.Contents.MarkedStrings.Should().HaveCount(2);
        }

        [Fact]
        public void Should_Deserialize_MarkupContent()
        {
            var hover = Serializer.DeserializeObject<Hover>(
                """{"contents":{"kind":"markdown","value":"# hi"}}"""
            );

            hover.Contents.HasMarkupContent.Should().BeTrue();
            hover.Contents.HasMarkedStrings.Should().BeFalse();
            hover.Contents.MarkupContent!.Kind.Should().Be(MarkupKind.Markdown);
            hover.Contents.MarkupContent!.Value.Should().Be("# hi");
        }

        [Fact]
        public void Should_Deserialize_Bare_String_As_MarkedString()
        {
            var contents = Serializer.DeserializeObject<MarkedStringsOrMarkupContent>("\"abc\"");

            contents.HasMarkedStrings.Should().BeTrue();
            contents.MarkedStrings.Should().ContainSingle()
                    .Which.Value.Should().Be("abc");
        }

        [Fact]
        public void Should_RoundTrip_Scalar_MarkedString()
        {
            const string json = """{"language":"vhdl","value":"signal clk : std_logic"}""";

            var contents = Serializer.DeserializeObject<MarkedStringsOrMarkupContent>(json);

            Serializer.SerializeObject(contents).Should().Be(json);
        }
    }
}
