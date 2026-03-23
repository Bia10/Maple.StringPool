namespace Maple.StringPool;

/// <summary>
/// An immutable decoded entry from <c>StringPool</c>.
/// </summary>
/// <param name="Index">
///   Zero-based slot index; corresponds directly to the <c>SP[0x…]</c> notation
///   used throughout the reference documentation.
/// </param>
/// <param name="Value">Decrypted Latin-1 string value.</param>
public readonly record struct StringPoolEntry(uint Index, string Value)
{
    /// <summary>
    /// Formats the entry as <c>SP[0xNNN] (decimal): value</c>,
    /// matching the canonical SP-index notation.
    /// </summary>
    public override string ToString() => $"SP[0x{Index:X}] ({Index}): {Value}";
}
