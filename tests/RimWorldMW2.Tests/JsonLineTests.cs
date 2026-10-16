using System.Globalization;
using System.Threading;
using RimWorldMW2;
using Xunit;

namespace RimWorldMW2.Tests
{
    public class JsonLineTests
    {
        [Fact]
        public void ReadsTopLevelValues()
        {
            JsonLine line = JsonLine.Parse("{\"t\":\"player_move\",\"pawn\":\"Human7\",\"x\":50.1,\"z\":-20.3,\"yaw\":185,\"on\":false}");
            Assert.NotNull(line);
            Assert.Equal("player_move", line.Type);
            Assert.Equal("Human7", line.Str("pawn"));
            Assert.Equal(50.1f, line.Num("x"));
            Assert.Equal(-20.3f, line.Num("z"));
            Assert.Equal(185f, line.Num("yaw"));
            Assert.Equal("false", line.Str("on"));
            Assert.Null(line.Str("missing"));
            Assert.Null(line.Num("missing"));
            Assert.Null(line.Num("pawn"));
        }

        [Fact]
        public void ToleratesWhitespace()
        {
            JsonLine line = JsonLine.Parse("  { \"t\" : \"use\" ,\n \"pawn\" : \"Human12\" , \"n\" : 3 }  ");
            Assert.NotNull(line);
            Assert.Equal("use", line.Type);
            Assert.Equal("Human12", line.Str("pawn"));
            Assert.Equal(3f, line.Num("n"));
        }

        [Fact]
        public void KeepsNestedValuesAsText()
        {
            const string pawns = "[{\"id\":\"a\",\"tags\":[\"x]\",\"y}\"]},{\"id\":\"b\"}]";
            JsonLine line = JsonLine.Parse("{\"t\":\"snapshot\",\"pawns\":" + pawns + ",\"obj\":{\"k\":{\"n\":1}},\"after\":1}");
            Assert.NotNull(line);
            Assert.Equal(pawns, line.Str("pawns"));
            Assert.Equal("{\"k\":{\"n\":1}}", line.Str("obj"));
            Assert.Equal(1f, line.Num("after"));
        }

        [Fact]
        public void ReadsNumberArrays()
        {
            JsonLine line = JsonLine.Parse("{\"from\":[50.1, 1.6 ,20.3],\"empty\":[],\"mixed\":[1,\"a\"],\"exp\":[1e2,-2.5E-1]}");
            Assert.Equal(new[] { 50.1f, 1.6f, 20.3f }, line.Nums("from"));
            Assert.Empty(line.Nums("empty"));
            Assert.Null(line.Nums("mixed"));
            Assert.Equal(new[] { 100f, -0.25f }, line.Nums("exp"));
            Assert.Null(line.Nums("missing"));
        }

        [Fact]
        public void DecodesStringEscapes()
        {
            JsonLine line = JsonLine.Parse("{\"s\":\"a\\\"b\\\\c\\/d\\ne\\tf\\u00e9\\u0041\"}");
            Assert.Equal("a\"b\\c/d\ne\tf\u00e9A", line.Str("s"));
        }

        [Fact]
        public void LaterDuplicateKeysWin()
        {
            Assert.Equal("2", JsonLine.Parse("{\"k\":1,\"k\":2}").Str("k"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[1,2]")]
        [InlineData("\"t\"")]
        [InlineData("{")]
        [InlineData("{\"t\":\"tick\"")]
        [InlineData("{\"t\" \"tick\"}")]
        [InlineData("{t:\"tick\"}")]
        [InlineData("{\"t\":\"unterminated}")]
        [InlineData("{\"a\":[1,2}")]
        public void RejectsMalformedLines(string text)
        {
            Assert.Null(JsonLine.Parse(text));
        }

        [Fact]
        public void NumbersIgnoreTheMachineCulture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                JsonLine line = JsonLine.Parse("{\"x\":42.5,\"at\":[28.5,33.5]}");
                Assert.Equal(42.5f, line.Num("x"));
                Assert.Equal(new[] { 28.5f, 33.5f }, line.Nums("at"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
