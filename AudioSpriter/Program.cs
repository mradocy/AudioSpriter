using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SF.Core;

// https://github.com/naudio/NAudio


/*

Takes all .wav files in the source directory, makes audio sprites and a json file of sound infos and puts them in the output directory

*/

// Example args:
// -s ../../../Test/inputs -d ../../../Test/outputs -w ../../../Test/wav-audiosprites -max 60

namespace AudioSpriter {

    class Program {

        private const double Mp3Delay = .0269; // (not exactly the same as 1056d / 44100)

        /// <summary>
        /// Default directory to place .wav versions of audio sprites.
        /// </summary>
        private const string DefaultWavDirectory = "./wav-audiosprites";

        /// <summary>
        /// Default max duration of audio sprites (in seconds).
        /// </summary>
        private const double DefaultAudioSpriteMaxDuration = 600;

        /// <summary>
        /// Default location of ffmpeg.exe
        /// </summary>
        private const string DefaultFFMpegFile = "ffmpeg.exe";

        public static void Main(string[] args) {

            // command line processing
            CommandLine.RegisterArg("-s", CommandLine.ParamType.ExistingDirectory, "./sourceDirectory", true, "The source directory containing the .wav files.");
            CommandLine.RegisterArg("-d", CommandLine.ParamType.ExistingDirectory, "\"D:/some/output/directory\"", true, "Output directory to place the created audio sprites and .json info files.");
            CommandLine.RegisterArg("-w", CommandLine.ParamType.Directory, ".wavDirectory", false, $"Directory to place the .wav versions of the audio sprites.  Default is {DefaultWavDirectory}");
            CommandLine.RegisterArg("-max", CommandLine.ParamType.Double, "600", false, $"Sets maximum length of an audio sprite, in seconds.  Default is {DefaultAudioSpriteMaxDuration}");
            CommandLine.RegisterArg("-ff", CommandLine.ParamType.ExistingFilePath, "tools/ffmpeg.exe", false, $"Sets location of ffmpeg.exe, program used to convert .mp3 to .ogg.  Default is {DefaultFFMpegFile}");

            if (!CommandLine.Process(args)) {
                ConsoleUtils.PauseIfConsoleWillBeDestroyed();
                return;
            }

            // starting audio spriter

            Console.WriteLine("== AudioSpriter Start ==");

            MediaFoundationApi.Startup(); // for creating mp3s

            string source = CommandLine.GetArgValue("-s");
            string destination = CommandLine.GetArgValue("-d");
            string baseFileName = "audioSprite";
            string wavsDirectory = CommandLine.GetArgValue("-w", DefaultWavDirectory);
            string ffmpegFilePath = CommandLine.GetArgValue("-ff", DefaultFFMpegFile);
            int sampleRate = 44100;
            int numChannels = 1;
            double spacingDuration = .1;
            double maxAudioSpriteDuration = CommandLine.GetArgValueDouble("-max", DefaultAudioSpriteMaxDuration);

            bool error = false;
            if (spacingDuration <= Mp3Delay) {
                Console.WriteLine("Spacing duration must be longer than " + Mp3Delay + ".");
                error = true;
            }

            if (error) {
                Console.ReadLine();
                return;
            }

            Directory.CreateDirectory(wavsDirectory);

            CreateAudioSprites(source, destination, baseFileName, wavsDirectory, ffmpegFilePath, sampleRate, numChannels, spacingDuration, maxAudioSpriteDuration);

            Console.WriteLine("== AudioSpriter End ==");
            ConsoleUtils.PauseIfConsoleWillBeDestroyed();

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

        public static void CreateAudioSprites(string sourceDirectory, string destinationDirectory, string baseFileName, string wavsDirectory, string ffmpegFilePath, int sampleRate, int numChannels, double spacingDuration, double maxAudioSpriteDuration) {

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
            for (int i = 0; i < soundInfos.Count; i++) {
                SoundInfo si = soundInfos[i];

                if (siSetDuration + si.duration + spacingDuration > maxAudioSpriteDuration) {
                    // sound doesn't fit.  Turn current set into an audio sprite and reset
                    CreateAudioSprite(siSet, audioSpriteCount, destinationDirectory, baseFileName, ffmpegFilePath, wavsDirectory, spacingDuration);
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
            CreateAudioSprite(siSet, audioSpriteCount, destinationDirectory, baseFileName, ffmpegFilePath, wavsDirectory, spacingDuration);

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
            for (int i = 0; i < soundInfos.Count; i++) {
                SoundInfo si = soundInfos[i];
                AudioSpriteJSON.Sound sound = new AudioSpriteJSON.Sound();
                sound.filename = si.displayFilename.Replace('\\', '/');
                sound.asIndex = si.audioSpriteIndex;
                sound.startTime = (float)si.startTime;
                sound.duration = (float)si.duration;
                asJSON.sounds[i] = sound;
            }
            JsonSerializerOptions serializeOptions = new JsonSerializerOptions() {
                IncludeFields = true
            };
            string json = JsonSerializer.Serialize(asJSON, serializeOptions);
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

        private static void CreateAudioSprite(List<SoundInfo> soundInfos, int audioSpriteIndex, string destinationDirectory, string baseFileName, string ffmpegFilePath, string wavsDirectory, double spacingDuration) {

            string fileNameNoExt = baseFileName + "_" + audioSpriteIndex;
            Console.WriteLine(fileNameNoExt + ":");
            foreach (SoundInfo si in soundInfos) {
                Console.WriteLine("+ " + si.displayFilename + " start time: " + si.startTime + " duration: " + si.duration);
            }

            // tell each soundInfo the audio sprite they'll belong to
            foreach (SoundInfo si in soundInfos) {
                si.audioSpriteIndex = audioSpriteIndex;
            }

            ISampleProvider[] providersMp3 = new ISampleProvider[soundInfos.Count];
            ISampleProvider[] providersOgg = new ISampleProvider[soundInfos.Count];
            for (int i = 0; i < soundInfos.Count; i++) {
                providersMp3[i] = soundInfos[i].audioReaderMp3;
                providersOgg[i] = soundInfos[i].audioReaderOgg;
            }

            ApplyOffset(providersMp3, spacingDuration, true);
            ApplyOffset(providersOgg, spacingDuration, false);

            // concatinate sounds into one sample provider
            ConcatenatingSampleProvider concatMp3 = new ConcatenatingSampleProvider(providersMp3);
            ConcatenatingSampleProvider concatOgg = new ConcatenatingSampleProvider(providersOgg);

            // write to file
            string mp3File = Path.Combine(destinationDirectory, fileNameNoExt + ".mp3");
            CreateMp3File(concatMp3, mp3File);
            string oggFile = Path.Combine(destinationDirectory, fileNameNoExt + ".ogg");
            string wavFile = Path.Combine(wavsDirectory, fileNameNoExt + ".wav");
            CreateOggFile(concatOgg, ffmpegFilePath, wavFile, oggFile);

        }

        private static void ApplyOffset(ISampleProvider[] providers, double spacingDuration, bool isMp3) {

            for (int i = 0; i < providers.Length; i++) {

                ISampleProvider sampleProvider = providers[i];

                OffsetSampleProvider offsetSampleProvider = new OffsetSampleProvider(sampleProvider);
                if (i == 0 && isMp3) {
                    // reduce padding to offset that delay .mp3 files have at the start
                    offsetSampleProvider.DelayBy = TimeSpan.FromSeconds(spacingDuration - Mp3Delay);
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

        private static void CreateMp3File(ISampleProvider provider, string fileName) {

            // assumes MediaFoundationApi.Startup() was already called

            SampleToWaveProvider audioSpriteWave = new SampleToWaveProvider(provider);

            try {

                MediaFoundationEncoder.EncodeToMp3(audioSpriteWave, fileName);

            } catch (InvalidOperationException ex) {
                Console.WriteLine(ex.Message);
            }

        }

        private static void CreateOggFile(ISampleProvider provider, string ffmpegFilePath, string wavFileName, string oggFileName) {

            string fullWavName = Path.GetFullPath(wavFileName);
            string fullOggName = Path.GetFullPath(oggFileName);

            // create .wav file
            WaveFileWriter.CreateWaveFile16(fullWavName, provider);

            // use external ffmpeg to convert to .ogg
            Console.WriteLine("(ffmpeg start)");

            // TODO: could running a process be part of SystemUtils?

            // get ffmpeg process file path
            string ffmpegFileName = SystemUtils.Where(ffmpegFilePath);
            if (ffmpegFileName == null) {
                throw new ArgumentException($"Could not find ffmpeg application \"{ffmpegFilePath}\"", nameof(ffmpegFilePath));
            }

            // run ffmpeg process
            Process ffmpegProcess = new Process();
            ffmpegProcess.StartInfo = new ProcessStartInfo() {
                FileName = ffmpegFileName,
                Arguments = "-i \"" + fullWavName + "\" -c:a libvorbis -qscale:a 6 -y \"" + fullOggName + "\"",
            };
            try {
                ffmpegProcess.Start();
            } catch (System.ComponentModel.Win32Exception ex) {
                throw new Exception($"Error running ffmpeg process: \"{ex.Message.Trim()}\".", ex);
            }

            // wait for process to complete
            //if (!ffmpegProcess.WaitForExit((int)(ZipProcessTimeout * 1000))) {
            //    ffmpegProcess.Kill();
            //    throw new ZipException("Killed 7-Zip process because it is taking too long.");
            //}
            ffmpegProcess.WaitForExit();
            int exitCode = ffmpegProcess.ExitCode;
            if (exitCode != 0) {
                throw new Exception($"ffmpeg process failed.  Exit code: {exitCode}");
            }

            Console.WriteLine("(ffmpeg end)");
        }

    }
}
