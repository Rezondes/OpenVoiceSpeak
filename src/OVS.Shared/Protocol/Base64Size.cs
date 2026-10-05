namespace OVS.Shared.Protocol;

/// <summary>
/// Base64 lengths. Base64 input is decoded with Convert.TryFromBase64String into a buffer of Decoded(text.Length) bytes,
/// then sliced to the bytes written: the buffer is larger than the result whenever the text has padding.
/// </summary>
public static class Base64Size
{
    /// <summary>The length of the base64 text of that many bytes, padding included.</summary>
    public static int Encoded(int bytes) => (bytes + 2) / 3 * 4;

    /// <summary>The most bytes a base64 text of that length can hold (padding counts as bytes here).</summary>
    public static int Decoded(int chars) => chars * 3 / 4;
}
