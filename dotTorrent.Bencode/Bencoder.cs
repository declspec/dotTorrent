using System;
using Monda;

namespace dotTorrent.Bencode {
    public class Bencoder {
        private static Parser<byte, long> AsciiNumberParser = Parser.Is((byte)'0')
            .Or(Parser.Is((byte)'-').Optional()
                .FollowedBy(Parser.Is<byte>(b => (b >= '1' && b <= '9')))
                .FollowedBy(Parser.TakeWhile<byte>(b => (b >= '0' && b <= '9'), min: 0)))
            .TryMap((ParseResult<byte> res, ReadOnlySpan<byte> data, out long value) => {
                var span = data.Slice(res.Start, res.Length);
                var start = span[0] == '-' ? 1 : 0;

                // process as an unsigned long to do bounds checking, then convert to a signed long afterwards
                var unsigned = 0UL;

                for (var i = start; i < span.Length; ++i) {
                    var b = (byte)(span[i] - '0');
                    unsigned = (unsigned * 10) + b;

                    // Ensure that the character we just processed was actually a digit
                    // and that an overflow hasn't occurred in the signed range
                    if (b >= 10 || unsigned > long.MaxValue) {
                        value = 0;
                        return false;
                    }
                }

                value = span[0] == '-' ? -(long)unsigned : (long)unsigned;
                return true;
            });

        public static Parser<byte, long> IntegerParser = AsciiNumberParser
            .Between(Parser.Is((byte)'i'), Parser.Is((byte)'e'));

        private static Parser<byte, long> ByteStringPrefixParser = AsciiNumberParser.FollowedBy(Parser.Is((byte)':'));

        public static Parser<byte, Range> ByteStringParser = new Parser<byte, Range>((data, offset, trace) => {
            var prefixResult = ByteStringPrefixParser.Parse(data, offset, trace);

            // TODO: The 'int.MaxValue' test isn't mandated by the spec, but .NET doesn't allow
            // long indexes into most array types so it's easier to enforce here.
            if (!prefixResult.Success || (prefixResult.Start + prefixResult.Length + prefixResult.Value) > int.MaxValue)
                return ParseResult.Fail<Range>();

            var start = prefixResult.Start + prefixResult.Length;
            var remaining = data.Length - start;

            return remaining >= prefixResult.Value
                ? ParseResult.Success(new Range(start, (int)prefixResult.Value), prefixResult.Start, (int)(prefixResult.Length + prefixResult.Value))
                : ParseResult.Fail<Range>();
        });

        
        /*

        private static Parser<byte, long> IntegerParser = new Parser<byte, long>((data, offset, _) => {
            var start = 1;
            var slice = data.Slice(offset);
            var end = slice.IndexOf((byte)'e');

            if (end <= start || slice[0] != 'i')
                return ParseResult.Fail<long>();

            if (slice[start] == '-')
                start++;

            // handle the only case where the first character can be zero
            // it must be the *only* character, and not preceded by a '-'
            if (slice[start] == '0' && ((end - start) > 1 || start > 1)) 
                return ParseResult.Fail<long>();

            // process as an unsigned long to do bounds checking, then convert to a signed long afterwards
            var unsigned = 0UL;

            for (var i = start; i < end; ++i) {
                var val = (byte)(slice[i] - '0');
                unsigned = (unsigned * 10) + val;

                // Ensure that the character we just processed was actually a digit
                // and that an overflow hasn't occurred in the signed range
                if (val >= 10 || unsigned > long.MaxValue)
                    return ParseResult.Fail<long>();
            }

            var signed = slice[1] == '-' ? -(long)unsigned : (long)unsigned;
            return ParseResult.Success(signed, offset, (end - offset) + 1);
        });
        */
        public static long ReadLong(ReadOnlySpan<byte> source) {
            var result = IntegerParser.Parse(source);
            return result.Success ? result.Value : throw new FormatException("invalid integer");
        }
    }
}
