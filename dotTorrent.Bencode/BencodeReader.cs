using System;
using System.Runtime.CompilerServices;

namespace dotTorrent.Bencode {
    public struct BencodeReaderState {
        public BencodeTokenType TokenType { get; }
        internal BitStack Stack { get; }

        internal BencodeReaderState(BencodeTokenType tokenType, BitStack stack) {
            TokenType = tokenType;
            Stack = stack;
        }
    }

    public ref struct BencodeReader {
        private ReadOnlySpan<byte> _buffer;
        private BitStack _stack;
        private readonly bool _isFinalBlock;

        private int _consumed;
        private long _tokenStartIndex;
        //private BencodeTokenType _previousTokenType;
        private BencodeTokenType _tokenType;
        private ReadOnlySpan<byte> _value;
        private bool _inObject;
        
        private bool IsLastSpan => _isFinalBlock;

        public BencodeReaderState CurrentState => new BencodeReaderState(_tokenType, _stack);

        // Public props
        public BencodeTokenType TokenType => _tokenType;
        public long TokenStartIndex => _tokenStartIndex;
        public ReadOnlySpan<byte> Value => _value;
        public int BytesConsumed => _consumed;

        public BencodeReader(ReadOnlySpan<byte> data)
            : this(data, isFinalBlock: true, new BencodeReaderState()) { }

        public BencodeReader(ReadOnlySpan<byte> data, bool isFinalBlock, BencodeReaderState state) {
            _buffer = data;
            _isFinalBlock = isFinalBlock;
            _stack = state.Stack;

            _tokenStartIndex = 0;
            //_previousTokenType = state.PreviousTokenType;
            _tokenType = state.TokenType;
            _inObject = _stack.Peek();
            _consumed = 0;
            _value = default;
        }

        public bool Read() {
            if (!HasEnoughData()) {
                // Were we in the middle of a list/dictionary?
                if (IsLastSpan && _stack.CurrentDepth > 0)
                    throw new Exception("unexpected end of input");
                return false;
            }

            var next = _buffer[_consumed];

            // Special case reading dictionary keys
            // NOTE: it's impossible to enforce the lexicographical ordering
            //  of dictionary keys in this reader; it must be done by the consumer.
            if (_inObject && TokenType != BencodeTokenType.DictionaryKey && next != 'e') 
                return ReadByteString(BencodeTokenType.DictionaryKey);
            
            switch (next) {
                case (byte)'0':
                case (byte)'1':
                case (byte)'2':
                case (byte)'3':
                case (byte)'4':
                case (byte)'5':
                case (byte)'6':
                case (byte)'7':
                case (byte)'8':
                case (byte)'9':
                    return ReadByteString(BencodeTokenType.ByteString);
                case (byte)'i':
                    return ReadNumber();
                case (byte)'d':
                    return ReadStartDictionary();
                case (byte)'l':
                    return ReadStartList();
                case (byte)'e':
                    return ReadEndObject();
                default:
                    throw new Exception("expected token");
            }
        }

        public int GetInt32() {
            if (!TryGetInt32(out var value))
                throw new FormatException("invalid number");
            return value;
        }

        public long GetInt64() {
            if (!TryGetInt64(out var value))
                throw new FormatException("invalid number");
            return value;
        }

        public bool TryGetInt64(out long value) {
            if (TokenType != BencodeTokenType.Number)
                throw new InvalidOperationException("wrong type");

            return TryGetInt64(_value, long.MaxValue, out value);
        }

        public bool TryGetInt32(out int value) {
            if (TokenType != BencodeTokenType.Number)
                throw new InvalidOperationException("wrong type");

            return TryGetInt32(_value, out value);
        }

        private bool ReadStartDictionary() {
            // This should only be called when we already know that the current byte is 'd'
            if (_stack.CurrentDepth == 0)
                _stack.SetFirstBit();
            else
                _stack.PushTrue();

            _inObject = true;
            SetCurrentToken(BencodeTokenType.StartDictionary, 1, _consumed, 1);
            return true;
        }

        private bool ReadStartList() {
            // This should only be called when we already know that the current byte is 'l'
            if (_stack.CurrentDepth == 0)
                _stack.ResetFirstBit();
            else
                _stack.PushFalse();

            _inObject = false;
            SetCurrentToken(BencodeTokenType.StartList, 1, _consumed, 1);
            return true;
        }

        private bool ReadEndObject() {
            // This should only be called when we already know that the current byte is 'e'
            if (_stack.CurrentDepth == 0)
                throw new Exception("unexpected end-of-object");

            var type = _inObject ? BencodeTokenType.EndDictionary : BencodeTokenType.EndList;
            _inObject = _stack.Pop();
            
            SetCurrentToken(type, 1, _consumed, 1);
            return true;
        }

        private bool ReadNumber() {
            // need at least i<digit>e
            if (!HasEnoughData(3))
                return !IsLastSpan ? false : throw new Exception("ran out of data"); // TODO: Look at a better way of implementing this

            if (_buffer[_consumed] != 'i')
                throw new Exception("expected 'i' marker");

            var offset = _buffer[_consumed + 1] == '-' ? 2 : 1;

            if (!ReadDigits(_buffer, _consumed + offset, out var byteLen))
                return false;

            if (_buffer[_consumed + offset + byteLen] != 'e')
                throw new Exception("expected 'e' delimiter");

            SetCurrentToken(BencodeTokenType.Number, offset + byteLen + 1, _consumed + 1, byteLen + offset - 1);
            return true;
        }

        private bool ReadByteString(BencodeTokenType tokenType) {
            if (!ReadDigits(_buffer, _consumed, out var byteLen))
                return false;

            // ReadDigits always ensures that there is at least one non-digit character available
            // in the buffer following the number (or returns false).
            if (_buffer[_consumed + byteLen] != ':')
                throw new Exception("expected ':' delimiter");

            // ReadOnlySpan can only be indexed by 32-bit integers.
            if (!TryGetInt32(_buffer.Slice(_consumed, byteLen), out var len))
                throw new Exception("byte string oversized");

            // Do we have enough data to consume the byte string?
            if (_buffer.Length < (_consumed + byteLen + 1 + len))
                return false;

            SetCurrentToken(tokenType, byteLen + 1 + len, _consumed + byteLen + 1, len);
            return true;
        }

        private bool ReadDigits(in ReadOnlySpan<byte> data, int start, out int len) {
            var end = start;
            len = 0;

            for (; end < data.Length; ++end) {
                var b = data[end];
                if (b < '0' || b > '9')
                    break;
                ++len;
            }

            // All numbers are terminated by some form of delimiter
            // so if we reach the end of the buffer then the operation is incomplete.
            if (end == data.Length)
                return !IsLastSpan ? false : throw new Exception("ran out of data");

            if (len == 0)
                throw new Exception("expected number");

            if (data[start] == '0' && len > 1)
                throw new Exception("invalid leading zeros");

            return true;
        }

        private void SetCurrentToken(BencodeTokenType tokenType, int consumed, int start, int len) {
            _tokenStartIndex = _consumed;
            //_previousTokenType = _tokenType;
            _tokenType = tokenType;
            _value = _buffer.Slice(start, len);
            _consumed += consumed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasEnoughData(int min = 1) {
            return _consumed + min <= (uint)_buffer.Length;
        }

        private static bool TryGetInt32(in ReadOnlySpan<byte> data, out int value) {
            var result = TryGetInt64(data, int.MaxValue, out var longValue);
            value = result ? (int)longValue : 0;
            return result;
        }

        private static bool TryGetInt64(in ReadOnlySpan<byte> data, ulong maxValue, out long value) {
            var start = data[0] == '-' ? 1 : 0;

            // process as an unsigned long to do bounds checking, then convert to a signed long afterwards
            var unsigned = 0UL;

            for (var i = start; i < data.Length; ++i) {
                var b = (byte)(data[i] - '0');
                unsigned = (unsigned * 10) + b;

                // Ensure that the character we just processed was actually a digit
                // and that an overflow hasn't occurred in the signed range
                if (b >= 10 || unsigned > maxValue) {
                    value = 0;
                    return false;
                }
            }

            value = data[0] == '-' ? -(long)unsigned : (long)unsigned;
            return true;
        }


    }
}
