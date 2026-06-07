namespace DelaunyFabric.Core;

public readonly struct MarkerColor(byte r, byte g, byte b) : System.IEquatable<MarkerColor>
{
	public byte R { get; } = r;
	public byte G { get; } = g;
	public byte B { get; } = b;

	public bool IsBlack => R == 0 && G == 0 && B == 0;

	public bool Equals(MarkerColor other) => R == other.R && G == other.G && B == other.B;
	public override bool Equals(object? obj) => obj is MarkerColor c && Equals(c);
	public override int GetHashCode() => (R << 16) | (G << 8) | B;
}
