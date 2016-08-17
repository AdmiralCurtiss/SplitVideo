using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SplitVideo {
	public class FFProbeData : IComparable<FFProbeData> {
		public bool IsKeyFrame;
		public double TimestampInSecondsDbl;
		public UInt64 TimestampInMicroSeconds;
		public UInt64 TimestampInTimebase;
		public FFProbeData Previous;
		public FFProbeData Next;

		public string TimestampInSecondsStr {
			get {
				return FFProbeData.MicroSecToSecStr( TimestampInMicroSeconds );
			}
		}

		public static FFProbeData[] Parse( string output, bool keyframesOnly = false ) {
			List<FFProbeData> data = new List<FFProbeData>();
			byte[] outputArray = Encoding.UTF8.GetBytes( output );
			XmlReader jsonReader = JsonReaderWriterFactory.CreateJsonReader( outputArray, 0, outputArray.Length, Encoding.UTF8, new System.Xml.XmlDictionaryReaderQuotas(), null );
			XElement root = XElement.Load( jsonReader );

			foreach ( var frame in root.Element( "frames" ).Elements() ) {
				bool isKeyframe = frame.Element( "key_frame" ).Value == "1";
				if ( keyframesOnly && !isKeyframe ) { continue; }

				FFProbeData f = new FFProbeData();
				f.IsKeyFrame = isKeyframe;
				string timestampInSecondsStr = frame.Element( "pkt_pts_time" ).Value;
				f.TimestampInSecondsDbl = Double.Parse( timestampInSecondsStr, System.Globalization.CultureInfo.InvariantCulture );
				if ( f.TimestampInSecondsDbl < 0.0 ) { continue; }
				f.TimestampInMicroSeconds = ToMicroSec( timestampInSecondsStr );
				f.TimestampInTimebase = UInt64.Parse( frame.Element( "pkt_pts" ).Value );

				data.Add( f );
			}

			data[0].Previous = data[0];
			for ( int i = 0; i < data.Count - 1; ++i ) {
				data[i].Next = data[i + 1];
			}
			for ( int i = 1; i < data.Count; ++i ) {
				data[i].Previous = data[i - 1];
			}
			data[data.Count - 1].Next = data[data.Count - 1];

			return data.ToArray();
		}

		public static ulong ToMicroSec( string p ) {
			if ( p.Contains( '.' ) ) {
				int loc = p.IndexOf( '.' );
				string post = p.Substring( loc + 1 );
				while ( post.Length < 6 ) {
					post += "0";
				}
				string s = p.Substring( 0, loc ) + post.Substring( 0, 6 );
				return UInt64.Parse( s );
			} else {
				return UInt64.Parse( p );
			}
		}
		public static string MicroSecToSecStr( UInt64 us ) {
			return ( us / 1000000 ).ToString() + "." + ( us % 1000000 ).ToString( "D6" );

		}

		public override bool Equals( object obj ) {
			FFProbeData other = obj as FFProbeData;
			if ( other == null ) { return false; }
			return this.TimestampInTimebase == other.TimestampInTimebase;
		}

		public int CompareTo( FFProbeData other ) {
			return TimestampInTimebase.CompareTo( other.TimestampInTimebase );
		}
	}
}
