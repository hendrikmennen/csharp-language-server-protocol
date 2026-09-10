using FluentAssertions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using Xunit;

namespace Lsp.Tests.Models
{
    public class TextEditOrInsertReplaceEditTests
    {
        private static readonly LspSerializer Serializer = new(ClientVersion.Lsp3);

        [Fact]
        public void Should_Deserialize_InsertReplaceEdit()
        {
            const string json = """
                {"newText":"a","insert":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"replace":{"start":{"line":0,"character":0},"end":{"line":0,"character":2}}}
                """;

            var result = Serializer.DeserializeObject<TextEditOrInsertReplaceEdit>(json);

            result.IsInsertReplaceEdit.Should().BeTrue();
            result.IsTextEdit.Should().BeFalse();
            result.InsertReplaceEdit!.NewText.Should().Be("a");
            result.InsertReplaceEdit.Insert.Should().Be(new Range(0, 0, 0, 1));
            result.InsertReplaceEdit.Replace.Should().Be(new Range(0, 0, 0, 2));
        }

        [Fact]
        public void Should_Deserialize_TextEdit()
        {
            const string json = """
                {"newText":"a","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}
                """;

            var result = Serializer.DeserializeObject<TextEditOrInsertReplaceEdit>(json);

            result.IsTextEdit.Should().BeTrue();
            result.IsInsertReplaceEdit.Should().BeFalse();
            result.TextEdit!.NewText.Should().Be("a");
            result.TextEdit.Range.Should().Be(new Range(0, 0, 0, 1));
        }

        [Fact]
        public void Should_Deserialize_Null()
        {
            Serializer.DeserializeObject<TextEditOrInsertReplaceEdit?>("null").Should().BeNull();
        }

        [Fact]
        public void Should_RoundTrip_InsertReplaceEdit()
        {
            var model = new TextEditOrInsertReplaceEdit(
                new InsertReplaceEdit {
                    NewText = "a",
                    Insert = new Range(0, 0, 0, 1),
                    Replace = new Range(0, 0, 0, 2)
                }
            );

            var result = Serializer.DeserializeObject<TextEditOrInsertReplaceEdit>(Serializer.SerializeObject(model));

            result.IsInsertReplaceEdit.Should().BeTrue();
            result.InsertReplaceEdit.Should().BeEquivalentTo(model.InsertReplaceEdit);
        }

        [Fact]
        public void Should_RoundTrip_TextEdit()
        {
            var model = new TextEditOrInsertReplaceEdit(
                new TextEdit {
                    NewText = "a",
                    Range = new Range(0, 0, 0, 1)
                }
            );

            var result = Serializer.DeserializeObject<TextEditOrInsertReplaceEdit>(Serializer.SerializeObject(model));

            result.IsTextEdit.Should().BeTrue();
            result.TextEdit.Should().BeEquivalentTo(model.TextEdit);
        }

        [Fact]
        public void Should_Deserialize_CompletionItem_With_InsertReplaceEdit()
        {
            // Servers that see `insertReplaceSupport: true` (tsc among them) send insert/replace
            // edits for every completion item.
            const string json = """
                {"label":"a","textEdit":{"newText":"a","insert":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"replace":{"start":{"line":0,"character":0},"end":{"line":0,"character":2}}}}
                """;

            var result = Serializer.DeserializeObject<CompletionItem>(json);

            result.TextEdit!.IsInsertReplaceEdit.Should().BeTrue();
            result.TextEdit.InsertReplaceEdit!.Insert.Should().Be(new Range(0, 0, 0, 1));
            result.TextEdit.InsertReplaceEdit.Replace.Should().Be(new Range(0, 0, 0, 2));
        }
    }
}
