using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoStuff {
    public class AudioTrack {

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_name")]
        public string Codec { get; set; }
    }
}
