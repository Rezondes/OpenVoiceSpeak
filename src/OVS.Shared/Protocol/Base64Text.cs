namespace OVS.Shared.Protocol;

/// <summary>
/// Base64 lengths and the one way to decode base64 input without throwing, apart from the size-capped voice frames: TryDecode fills a buffer of Decoded(text.Length) bytes with
/// Convert.TryFromBase64String and slices it to the bytes written (the buffer is larger than the result whenever the text has padding).
/// </summary>
public static class Base64Text
{
    /// <summary>The length of the base64 text of that many bytes, padding included.</summary>
    public static int Encoded(int bytes) => (bytes + 2) / 3 * 4;

    /// <summary>The most bytes a base64 text of that length can hold (padding counts as bytes here).</summary>
    public static int Decoded(int chars) => chars * 3 / 4;

    /// <summary>
    /// Decodes base64 text; false (and empty bytes) for null or text that is not base64. A bool with an out value, not a
    /// nullable array, because every caller branches on failure and an empty array is a valid result (empty text).
    /// </summary>
    public static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = [];
        if (text is null) return false;
        var buffer = new byte[Decoded(text.Length)];
        if (!Convert.TryFromBase64String(text, buffer, out int written)) return false;
        bytes = buffer[..written];
        return true;
    }
}
