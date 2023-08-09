using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using SharpAudio;
using SharpAudio.Codec;

namespace Autodraw
{
    public class Audio
    {
        private static AudioEngine? audioEngine;

        // Linux is unsupported for the time being.
        public static SoundStream? PlaySound(string soundFileUrl, float volume = 0.5f)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return null;
            }
            if (audioEngine == null)
            {
                audioEngine = AudioEngine.CreateOpenAL();
            }
            try
            {
                SoundStream soundStream = new SoundStream(AssetLoader.Open(new Uri(soundFileUrl)), audioEngine);
            
                if(soundStream == null) { return null; };
                Thread.Sleep(1); // Don't ask, just know it fixes a bug... (I think it doesn't have us wait for the object to complete creation. :3)

                soundStream.Volume = volume;

                soundStream.Play();

                return soundStream;
            }
            catch
            {
                Utils.Log("Exception: Audio.PlaySound had encountered an error.");
            }
            return null;
        }
    }
}
