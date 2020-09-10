namespace dotTorrent.Bencode {
    public enum BencodeTokenType : byte {
        None,
        StartDictionary,
        EndDictionary,
        StartList,
        EndList,
        Number,
        DictionaryKey,
        ByteString
    }
}
