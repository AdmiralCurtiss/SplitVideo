using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitVideo {
	class Program {


		static StringBuilder cmdout = new StringBuilder( 1024 * 1024 * 20 );
		private static void OutputDataHandler( object sendingProcess, DataReceivedEventArgs outputLine ) {
			if ( outputLine.Data == null )
				return;
			cmdout.Append( outputLine.Data );
		}

		public static string RunProgram( string prog, string args, bool getOutput ) {
			Console.WriteLine( prog + " " + args );
			System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
			startInfo.CreateNoWindow = getOutput;
			startInfo.UseShellExecute = !getOutput;
			startInfo.FileName = prog;
			startInfo.WindowStyle = getOutput ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal;
			startInfo.Arguments = args;
			startInfo.RedirectStandardInput = getOutput;
			startInfo.RedirectStandardOutput = getOutput;
			startInfo.RedirectStandardError = false;

			using ( System.Diagnostics.Process process = new Process() ) {
				process.StartInfo = startInfo;

				if ( getOutput ) {
					process.OutputDataReceived += OutputDataHandler;
					process.Start();
					process.StandardInput.Flush();
					process.StandardInput.Close();
					process.BeginOutputReadLine();
				} else {
					process.Start();
				}
				process.WaitForExit();

				if ( process.ExitCode != 0 ) {
					Console.WriteLine( prog + " returned nonzero!" );
					throw new Exception( prog );
				}

				return getOutput ? cmdout.ToString() : null;
			}
		}
		public static string RunProgram( string prog, string[] args, bool getOutput ) {
			StringBuilder sb = new StringBuilder();
			foreach ( string s in args ) {
				sb.Append( '"' );
				sb.Append( s );
				sb.Append( '"' );
				sb.Append( ' ' );
			}
			sb.Remove( sb.Length - 1, 1 );
			return RunProgram( prog, sb.ToString(), getOutput );
		}

		static string TimeToSeconds( string time ) {
			UInt64 sec = 0;
			UInt64 ms = 0;

			if ( time.Contains( '.' ) ) {
				ms = UInt64.Parse( time.Substring( time.IndexOf( '.' ) + 1 ) );
				time = time.Substring( 0, time.IndexOf( '.' ) );
			}

			string[] elements = time.Split( ':' );
			UInt64[] multipliers = { 60, 60, 24 };
			int i = 0;
			foreach ( var e in elements.Reverse() ) {
				UInt64 ei = UInt64.Parse( e );
				for ( int j = 0; j < i; ++j ) {
					ei *= multipliers[j];
				}
				sec += ei;
				++i;
			}

			return sec.ToString() + "." + ms.ToString();
		}

		static void Main( string[] args ) {
			bool preferAfter = false;
			bool preferBefore = false;

			string filename = null;
			List<string> timestamps = new List<string>();
			for ( int i = 0; i < args.Length; ++i ) {
				if ( args[i] == "--before" ) { preferBefore = true; continue; }
				if ( args[i] == "--after" ) { preferAfter = true; continue; }

				if ( filename == null ) {
					filename = args[i];
				} else {
					timestamps.Add( TimeToSeconds( args[i] ) );
				}
			}

			if ( filename == null || timestamps.Count == 0 ) {
				Console.WriteLine( "Not enough arguments!" );
				return;
			}

			List<string> ffprobeArgs = new List<string>();
			ffprobeArgs.Add( "-v" );
			ffprobeArgs.Add( "quiet" );
			ffprobeArgs.Add( "-print_format" );
			ffprobeArgs.Add( "json" );
			ffprobeArgs.Add( "-show_frames" );
			ffprobeArgs.Add( "-skip_frame" );
			ffprobeArgs.Add( "nokey" );
			ffprobeArgs.Add( "-select_streams" );
			ffprobeArgs.Add( "v" );
			ffprobeArgs.Add( filename );

			string ffprobeOutput = RunProgram( "ffprobe", ffprobeArgs.ToArray(), true );
			FFProbeData[] ffprobeData = FFProbeData.Parse( ffprobeOutput, true );

			if ( ffprobeData.LongLength == 0 ) {
				Console.WriteLine( "No keyframes found!" );
				return;
			}

			// find best frame for wanted timestamp
			SortedSet<FFProbeData> framesToSplitOn = new SortedSet<FFProbeData>();
			foreach ( string ts in timestamps ) {
				UInt64 us = FFProbeData.ToMicroSec( ts );
				FFProbeData best = null;
				for ( long i = 1; i < ffprobeData.LongLength; ++i ) {
					// search for the first frame bigger or equal than wanted
					if ( us <= ffprobeData[i].TimestampInMicroSeconds ) {
						if ( us == ffprobeData[i].TimestampInMicroSeconds ) {
							// if equal we're good
							best = ffprobeData[i];
							break;
						}

						// otherwise grab current and previous to compare difference and grab the shorter one
						FFProbeData prev = ffprobeData[i - 1];
						FFProbeData curr = ffprobeData[i];

						if ( preferAfter ) { best = curr; break; }
						if ( preferBefore ) { best = prev; break; }

						UInt64 diffToPrev = prev.TimestampInMicroSeconds - us;
						UInt64 diffToCurr = us - curr.TimestampInMicroSeconds;
						if ( diffToPrev < diffToCurr ) {
							best = prev;
						} else {
							best = curr;
						}
						break;
					}
				}
				if ( best == null ) {
					// requested time is past last timestamp, use last keyframe
					best = ffprobeData.Last();
				}
				framesToSplitOn.Add( best );
			}

			List<FFProbeData> framesToSplitOnList = new List<FFProbeData>();
			foreach ( FFProbeData frame in framesToSplitOn ) {
				Console.WriteLine( frame.TimestampInSecondsStr );
				framesToSplitOnList.Add( frame );
			}

			// ffmpeg -i input.mp4 -codec copy -t 1:40:19 output-1.mp4 -codec copy -ss 1:40:19 output-2.mp4
			for ( int i = 0; i < framesToSplitOnList.Count + 1; ++i ) {
				List<string> ffmpegArgs = new List<string>();
				ffmpegArgs.Add( "-y" );
				if ( i != 0 ) {
					ffmpegArgs.Add( "-ss" );

					string targetTimestamp;
					UInt64 diff = framesToSplitOnList[i - 1].Next.TimestampInMicroSeconds - framesToSplitOnList[i - 1].TimestampInMicroSeconds;
					UInt64 target = framesToSplitOnList[i - 1].TimestampInMicroSeconds + diff / 2;
					ffmpegArgs.Add( FFProbeData.MicroSecToSecStr( target ) );
				}
				ffmpegArgs.Add( "-i" );
				ffmpegArgs.Add( filename );
				ffmpegArgs.Add( "-codec" );
				ffmpegArgs.Add( "copy" );
				if ( i != framesToSplitOn.Count ) {
					ffmpegArgs.Add( "-to" );
					ffmpegArgs.Add( framesToSplitOnList[i].TimestampInSecondsStr );
				}
				ffmpegArgs.Add( System.IO.Path.Combine( System.IO.Path.GetDirectoryName( filename ), System.IO.Path.GetFileNameWithoutExtension( filename ) + "-part" + ( i + 1 ) + ".mp4" ) );

				RunProgram( "ffmpeg", ffmpegArgs.ToArray(), false );
			}

			return;
		}
	}
}
