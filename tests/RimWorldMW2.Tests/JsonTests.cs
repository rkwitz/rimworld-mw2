using System.Globalization;
using System.Threading;
using RimWorldMW2;
using Xunit;

namespace RimWorldMW2.Tests
{
    public class JsonTests
    {
        [Fact]
        public void WritesTheExactHelloAndTickTheBridgeExpects()
        {
            // The same bytes bridge/crates/protocol checks in parses_rimworld_m0_lines.
            Assert.Equal("{\"t\":\"hello\",\"side\":\"rimworld\",\"protocol\":0,\"game_version\":\"1.6.4871 rev1234\"}",
                Json.Msg("hello").Str("side", "rimworld").Num("protocol", 0).Str("game_version", "1.6.4871 rev1234").ToString());
            Assert.Equal("{\"t\":\"tick\",\"tick\":600}", Json.Msg("tick").Num("tick", 600).ToString());
        }

        [Fact]
        public void WritesBoolsFloatsAndRawValues()
        {
            string json = Json.Msg("x").Bool("on", true).Bool("off", false).Num("f", 1.23456f).Num("whole", 2f)
                .Raw("at", "[1,2]").ToString();
            Assert.Equal("{\"t\":\"x\",\"on\":true,\"off\":false,\"f\":1.235,\"whole\":2,\"at\":[1,2]}", json);
        }

        [Fact]
        public void EmptyMessageIsAnEmptyObject()
        {
            Assert.Equal("{}", new Json().ToString());
        }

        [Fact]
        public void EscapesStrings()
        {
            string json = Json.Msg("s").Str("v", "a\"b\\c\nd\re\tf\u0001g").Str("none", null).ToString();
            Assert.Equal("{\"t\":\"s\",\"v\":\"a\\\"b\\\\c\\nd\\re\\tf\\u0001g\",\"none\":\"\"}", json);
        }

        [Fact]
        public void NumbersIgnoreTheMachineCulture()
        {
            // Players run RimWorld with their own locale; a comma decimal would break every message.
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("{\"x\":42.5,\"n\":-1234567}", new Json().Num("x", 42.5f).Num("n", -1234567L).ToString());
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void RoundTripsThroughJsonLine()
        {
            string text = "quote \" slash \\ newline \n tab \t unicode é";
            JsonLine line = JsonLine.Parse(Json.Msg("echo").Str("text", text).Num("n", 7.25f).Bool("b", true)
                .Raw("v", "[1.5,0,-2]").ToString());
            Assert.NotNull(line);
            Assert.Equal("echo", line.Type);
            Assert.Equal(text, line.Str("text"));
            Assert.Equal(7.25f, line.Num("n"));
            Assert.Equal("true", line.Str("b"));
            Assert.Equal(new[] { 1.5f, 0f, -2f }, line.Nums("v"));
        }
    }
}
