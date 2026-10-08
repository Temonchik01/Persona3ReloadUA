namespace P3RPlgTool;

readonly record struct PlgVertex(float X, float Y, float Z);

readonly record struct PlgEntry(
    string Name,
    List<(float X, float Y, float Z)> Vertices,
    List<ushort> Indices,
    List<uint> Colors,
    float MinX, float MinY, float MaxX, float MaxY);
