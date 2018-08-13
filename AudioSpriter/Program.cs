using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using NAudio.MediaFoundation;
using System.Web.Script.Serialization;

// https://github.com/naudio/NAudio


/*

audiospriter -s ./sounds -d "D:/some/output/directory"

Takes all .wav files in the source directory, makes audio sprites and a json file of sound infos and puts them in the output directory

*/

namespace AudioSpriter {

    class Program {

        const double MP3_DELAY = .0269; // (not exactly the same as 1056d / 44100)

        public static void Main(string[] args) {

            if (args.Length <= 0) {

                Console.Clear();
                Console.WriteLine("AudioSpriter");
                Console.WriteLine();
                Console.WriteLine("audiospriter -s ./sourceDirectory -d \"D:/some/output/directory\"");
                Console.WriteLine();
                Console.WriteLine("- Finds all .wav files in the source directory,\n  then in the destination directory makes audio sprites and a .json of info for them.");
                Console.WriteLine("- All .wav files must have a sample rate of 44100 and be mono channel.");
                Console.WriteLine();
                Console.WriteLine("Other args:");
                Console.WriteLine("- Set directory to place the .wav versions of the audio sprites: -w ./wavDirectory");
                Console.WriteLine("- Set maximum length of an audio sprite (in seconds): -max 600");

                Console.ReadLine();
                return;
            }

            Console.WriteLine("AudioSpriter Start:");
            Console.WriteLine();

            MediaFoundationApi.Startup(); // for creating mp3s

            string source = "";
            string destination = "";
            string baseFileName = "audioSprite";
            string wavsDirectory = "./wav-audiosprites";
            int sampleRate = 44100;
            int numChannels = 1;
            double spacingDuration = .1;
            double maxAudioSpriteDuration = 600;

            string str = "";
            for (int i = 0; i < args.Length; i++) {
                string arg = args[i];
                if (arg == "-s") {
                    source = i + 1 < args.Length ? args[i + 1] : "";
                } else if (arg == "-d") {
                    destination = i + 1 < args.Length ? args[i + 1] : "";
                } else if (arg == "-max") {
                    str = i + 1 < args.Length ? args[i + 1] : "";
                    double outD = 0;
                    if (double.TryParse(str, out outD)) {
                        maxAudioSpriteDuration = outD;
                    }
                } else if (arg == "-w") {
                    wavsDirectory = i + 1 < args.Length ? args[i + 1] : "";
                }
            }

            bool error = false;
            if (source == "") {
                Console.WriteLine("Must specify source with -s ./sourceDirectory");
                error = true;
            } else if (!Directory.Exists(source)) {
                Console.WriteLine("Source directory " + source + " doesn't exist.");
                error = true;
            }
            
            if (destination == "") {
                Console.WriteLine("Must specifiy destination with -d \"D:/some/output/directory\"");
                error = true;
            } else if (!Directory.Exists(destination)) {
                Console.WriteLine("Destination directory " + destination + " doesn't exist.");
                error = true;
            }

            if (wavsDirectory == "") {
                Console.WriteLine("Must have a valid directory to place .wav versions of the audio sprites.");
                error = true;
            }

            if (spacingDuration <= MP3_DELAY) {
                Console.WriteLine("Spacing duration must be longer than " + MP3_DELAY + ".");
                error = true;
            }

            if (error) {
                Console.ReadLine();
                return;
            }

            Directory.CreateDirectory(wavsDirectory);

            createAudioSprites(source, destination, baseFileName, wavsDirectory, sampleRate, numChannels, spacingDuration, maxAudioSpriteDuration);

            Console.ReadLine();

        }

        public class SoundInfo {
            public string file = "";
            public string displayFilename = "";
            public double duration = 0;
            public AudioFileReader audioReaderMp3 = null;
            public AudioFileReader audioReaderOgg = null;
            // to be filled by audio sprites:
            public double startTime = 0; 
            public int audioSpriteIndex = 0;
            public override string ToString() {
                return displayFilename + " - audioSpriteIndex: " + audioSpriteIndex + " startTime: " + startTime + " duration: " + duration;
            }
        }

        public static void createAudioSprites(string sourceDirectory, string destinationDirectory, string baseFileName, string wavsDirectory, int sampleRate, int numChannels, double spacingDuration, double maxAudioSpriteDuration) {

            string[] inputFiles = Directory.GetFiles(sourceDirectory, "*.wav", SearchOption.AllDirectories);
            if (inputFiles.Length <= 0) {
                Console.WriteLine("No .wav files found in " + sourceDirectory);
                return;
            }

            bool error = false;
            string sourceFullPath = Path.GetFullPath(sourceDirectory);
            List<SoundInfo> soundInfos = new List<SoundInfo>();
            List<AudioFileReader> audioReaders = new List<AudioFileReader>();
            
            // create soundInfos
            foreach (string file in inputFiles) {

                SoundInfo soundInfo = new SoundInfo();
                soundInfos.Add(soundInfo);
                soundInfo.file = file;
                soundInfo.displayFilename = Path.GetFullPath(file).Substring(sourceFullPath.Length + 1);
                
                // create audio readers
                AudioFileReader audioReaderMp3 = new AudioFileReader(file);
                audioReaders.Add(audioReaderMp3);
                soundInfo.audioReaderMp3 = audioReaderMp3;
                AudioFileReader audioReaderOgg = new AudioFileReader(file);
                audioReaders.Add(audioReaderOgg);
                soundInfo.audioReaderOgg = audioReaderOgg;

                WaveFormat waveFormat = audioReaderMp3.WaveFormat;
                if (waveFormat.SampleRate != sampleRate) {
                    Console.WriteLine(file + " must have a sample rate of " + sampleRate);
                    error = true;
                }
                if (waveFormat.Channels != numChannels) {
                    if (numChannels == 1) {
                        Console.WriteLine(file + " must be mono channel.");
                    } else {
                        Console.WriteLine(file + " must have " + numChannels + " channels");
                    }
                    error = true;
                }

                soundInfo.duration = audioReaderMp3.TotalTime.TotalSeconds;
                if (soundInfo.duration > maxAudioSpriteDuration - spacingDuration * 2) {
                    Console.WriteLine(file + " is too long.  With current parameters sound length must be shorter than " + (maxAudioSpriteDuration - spacingDuration * 2) + " seconds.");
                    error = true;
                }
            }
            if (error) {
                foreach (AudioFileReader audioReader in audioReaders) {
                    audioReader.Dispose();
                }
                return;
            }

            // split soundInfos such that each part has a total length less than maxAudioSpriteDuration
            List<SoundInfo> siSet = new List<SoundInfo>();
            double siSetDuration = spacingDuration;
            int audioSpriteCount = 0;
            for (int i=0; i < soundInfos.Count; i++) {
                SoundInfo si = soundInfos[i];

                if (siSetDuration + si.duration + spacingDuration > maxAudioSpriteDuration) {
                    // sound doesn't fit.  Turn current set into an audio sprite and reset
                    createAudioSprite(siSet, audioSpriteCount, destinationDirectory, baseFileName, wavsDirectory, spacingDuration);
                    siSet.Clear();
                    siSetDuration = spacingDuration;
                    audioSpriteCount++;
                }
                
                // add to set
                siSet.Add(si);
                si.startTime = siSetDuration;
                siSetDuration += si.duration + spacingDuration;
                si.audioSpriteIndex = audioSpriteCount;
                
            }
            // create last audio sprite
            createAudioSprite(siSet, audioSpriteCount, destinationDirectory, baseFileName, wavsDirectory, spacingDuration);

            foreach (AudioFileReader audioReader in audioReaders) {
                audioReader.Dispose();
            }

            // make .json
            AudioSpriteJSON asJSON = new AudioSpriteJSON();
            asJSON.audioSprites = new AudioSpriteJSON.AudioSprite[audioSpriteCount + 1];
            for (int i = 0; i < audioSpriteCount + 1; i++) {
                AudioSpriteJSON.AudioSprite audioSprite = new AudioSpriteJSON.AudioSprite();
                audioSprite.mp3 = baseFileName + "_" + i + ".mp3";
                audioSprite.ogg = baseFileName + "_" + i + ".ogg";
                asJSON.audioSprites[i] = audioSprite;
            }
            asJSON.sounds = new AudioSpriteJSON.Sound[soundInfos.Count];
            for (int i=0; i < soundInfos.Count; i++) {
                SoundInfo si = soundInfos[i];
                AudioSpriteJSON.Sound sound = new AudioSpriteJSON.Sound();
                sound.filename = si.displayFilename.Replace('\\','/');
                sound.asIndex = si.audioSpriteIndex;
                sound.startTime = (float)si.startTime;
                sound.duration = (float)si.duration;
                asJSON.sounds[i] = sound;
            }
            string json = new JavaScriptSerializer().Serialize(asJSON);
            File.WriteAllText(Path.Combine(destinationDirectory, "audioSprites.json"), json);
            
            Console.WriteLine("Completed with no errors.");

        }

        private class AudioSpriteJSON {
            
            public AudioSprite[] audioSprites;

            public class AudioSprite {
                public string mp3;
                public string ogg;
            }

            public Sound[] sounds;

            public class Sound {
                public string filename;
                public int asIndex;
                public float startTime;
                public float duration;
            }
            
        }

        private static void createAudioSprite(List<SoundInfo> soundInfos, int audioSpriteIndex, string destinationDirectory, string baseFileName, string wavsDirectory, double spacingDuration) {
            
            string fileNameNoExt = baseFileName + "_" + audioSpriteIndex;
            Console.WriteLine(fileNameNoExt + ":");
            foreach (SoundInfo si in soundInfos) {
                Console.WriteLine("- " + si.displayFilename + " start time: " + si.startTime + " duration: " + si.duration);
            }

            // tell each soundInfo the audio sprite they'll belong to
            foreach (SoundInfo si in soundInfos) {
                si.audioSpriteIndex = audioSpriteIndex;
            }

            ISampleProvider[] providersMp3 = new ISampleProvider[soundInfos.Count];
            ISampleProvider[] providersOgg = new ISampleProvider[soundInfos.Count];
            for (int i=0; i < soundInfos.Count; i++) {
                providersMp3[i] = soundInfos[i].audioReaderMp3;
                providersOgg[i] = soundInfos[i].audioReaderOgg;
            }

            applyOffset(providersMp3, spacingDuration, true);
            applyOffset(providersOgg, spacingDuration, false);

            // concatinate sounds into one sample provider
            ConcatenatingSampleProvider concatMp3 = new ConcatenatingSampleProvider(providersMp3);
            ConcatenatingSampleProvider concatOgg = new ConcatenatingSampleProvider(providersOgg);

            // write to file
            string mp3File = Path.Combine(destinationDirectory, fileNameNoExt + ".mp3");
            createMp3File(concatMp3, mp3File);
            string oggFile = Path.Combine(destinationDirectory, fileNameNoExt + ".ogg");
            string wavFile = Path.Combine(wavsDirectory, fileNameNoExt + ".wav");
            createOggFile(concatOgg, wavFile, oggFile);

        }

        private static void applyOffset(ISampleProvider[] providers, double spacingDuration, bool isMp3) {

            for (int i = 0; i < providers.Length; i++) {

                ISampleProvider sampleProvider = providers[i];

                OffsetSampleProvider offsetSampleProvider = new OffsetSampleProvider(sampleProvider);
                if (i == 0 && isMp3) {
                    // reduce padding to offset that delay .mp3 files have at the start
                    offsetSampleProvider.DelayBy = TimeSpan.FromSeconds(spacingDuration - MP3_DELAY);
                } else {
                    offsetSampleProvider.DelayBy = TimeSpan.FromSeconds(spacingDuration);
                }
                if (i == providers.Length - 1) {
                    // pad out end of the audio sprite too.
                    offsetSampleProvider.LeadOut = TimeSpan.FromSeconds(spacingDuration);
                }

                providers[i] = offsetSampleProvider;
            }

        }

        private static void createMp3File(ISampleProvider provider, string fileName) {

            // assumes MediaFoundationApi.Startup() was already called

            SampleToWaveProvider audioSpriteWave = new SampleToWaveProvider(provider);

            try {

                MediaFoundationEncoder.EncodeToMp3(audioSpriteWave, fileName);

            } catch (InvalidOperationException ex) {
                Console.WriteLine(ex.Message);
            }

        }

        private static void createOggFile(ISampleProvider provider, string wavFileName, string oggFileName) {
            
            string fullWavName = Path.GetFullPath(wavFileName);
            string fullOggName = Path.GetFullPath(oggFileName);

            // create .wav file
            WaveFileWriter.CreateWaveFile16(fullWavName, provider);

            // use external ffmpeg to convert to .ogg
            string strCmdText = "/C " + "ffmpeg -i \"" + fullWavName + "\" -c:a libvorbis -qscale:a 6 -y \"" + fullOggName + "\"";
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            //startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = strCmdText;
            process.StartInfo = startInfo;
            process.Start();

        }

    }

}
