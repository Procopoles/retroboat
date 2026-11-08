using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Retroboat.Downloads;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>O formato que o ES do Retroboat (RB-010) lê em POST /retroboat/download.</summary>
    public class DownloadStatusJsonTests
    {
        static Dictionary<string, object> Parse(string json) => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);

        static DownloadItem Item() => new DownloadItem
        {
            System = "ps2",
            CloudName = "Jogo (USA).zip",
            LocalName = "Jogo (USA).iso",
            Title = "Tom Clancy's Splinter Cell: Pandora, a Ação"
        };

        [Fact]
        public void Downloading_carries_the_game_as_the_es_sees_it_and_the_progress()
        {
            var json = Parse(DownloadStatusJson.Of(new DownloadStatus
            {
                Item = Item(),
                Phase = DownloadPhase.Downloading,
                BytesDone = 5_000_000_000,
                BytesTotal = 9_000_000_000,
                BytesPerSecond = 12_345_678.91,
                Remaining = TimeSpan.FromSeconds(190.4)
            }));

            Assert.Equal("downloading", json["phase"]);
            Assert.Equal("ps2", json["system"]);
            Assert.Equal("Jogo (USA).iso", json["local"]);
            Assert.Equal("Tom Clancy's Splinter Cell: Pandora, a Ação", json["title"]);
            Assert.Equal(5_000_000_000L, Convert.ToInt64(json["done"]));
            Assert.Equal(9_000_000_000L, Convert.ToInt64(json["total"]));
            Assert.Equal(12_345_678.9, Convert.ToDouble(json["speed"]), 1);
            Assert.Equal(190L, Convert.ToInt64(json["remaining"]));
            Assert.Equal("", json["message"]);
        }

        [Theory]
        [InlineData(DownloadPhase.Preparing, "preparing")]
        [InlineData(DownloadPhase.Downloading, "downloading")]
        [InlineData(DownloadPhase.WaitingForCloud, "waiting")]
        [InlineData(DownloadPhase.Finished, "finished")]
        [InlineData(DownloadPhase.Failed, "failed")]
        public void Phases_use_the_names_the_es_knows(DownloadPhase phase, string name)
        {
            Assert.Equal(name, Parse(DownloadStatusJson.Of(new DownloadStatus { Item = Item(), Phase = phase }))["phase"]);
        }

        [Fact]
        public void Without_an_estimate_remaining_is_minus_one_and_failed_carries_the_reason()
        {
            var json = Parse(DownloadStatusJson.Of(new DownloadStatus
            {
                Item = Item(),
                Phase = DownloadPhase.Failed,
                Message = "Este jogo está corrompido na nuvem: o arquivo está zerado."
            }));

            Assert.Equal(-1L, Convert.ToInt64(json["remaining"]));
            Assert.Equal("Este jogo está corrompido na nuvem: o arquivo está zerado.", json["message"]);
        }

        [Fact]
        public void Nothing_downloading_is_idle()
        {
            var json = Parse(DownloadStatusJson.Of(null));

            Assert.Equal(DownloadStatusJson.Idle, json["phase"]);
            Assert.Equal("", json["system"]);
            Assert.Equal("", json["local"]);
            Assert.Equal(-1L, Convert.ToInt64(json["remaining"]));
        }
    }
}
