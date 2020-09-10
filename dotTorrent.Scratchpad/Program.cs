using System;
using System.Linq;
using System.Text;
using dotTorrent.Bencode;

namespace dotTorrent.Scratchpad {
    class Program {
        static void Main(string[] args) {
            //var data = Encoding.ASCII.GetBytes("i-50123ed3:fooi76ee");
            var data = System.IO.File.ReadAllBytes(@"C:\Users\254288b\Downloads\file");
            var reader = new BencodeReader(data);

            while (reader.Read()) {
                switch (reader.TokenType) {
                    case BencodeTokenType.DictionaryKey:
                    case BencodeTokenType.ByteString:
                        Console.WriteLine("{0} {1}", reader.TokenType, new string(Array.ConvertAll(Encoding.ASCII.GetString(reader.Value).ToCharArray(), c => char.IsControl(c) ? '.' : c)));
                        break;
                    case BencodeTokenType.Number:
                        Console.WriteLine("{0} {1}", reader.TokenType, reader.GetInt64());
                        break;
                    default:
                        Console.WriteLine("{0}", reader.TokenType);
                        break;
                }
            }
        }
    }
}
