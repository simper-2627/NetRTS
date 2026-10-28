using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetRts.Protocol;

/// <summary>
/// A unit, building or upgrade name. The names come from the server's rules file (GET /api/v1/rules),
/// so there can be more than the well-known ones below. On the wire it is a plain string. Names compare
/// case-insensitively, as the enums these replaced did when parsed.
/// </summary>
public interface ITypeId<TSelf> : IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : struct, ITypeId<TSelf>
{
    string Name { get; }

    static abstract TSelf From(string name);
}

[JsonConverter(typeof(TypeIdConverter<UnitType>))]
public readonly record struct UnitType(string Name) : ITypeId<UnitType>
{
    public static UnitType Worker { get; } = new("Worker");
    public static UnitType Soldier { get; } = new("Soldier");
    public static UnitType Archer { get; } = new("Archer");
    public static UnitType Scout { get; } = new("Scout");
    public static UnitType Trebuchet { get; } = new("Trebuchet");

    public static UnitType From(string name) => new(name);

    public static implicit operator UnitType(string name) => new(name);

    public bool Equals(UnitType other) => TypeId.Equal(Name, other.Name);

    public override int GetHashCode() => TypeId.Hash(Name);

    public int CompareTo(UnitType other) => TypeId.Compare(Name, other.Name);

    public override string ToString() => Name ?? "";
}

[JsonConverter(typeof(TypeIdConverter<BuildingType>))]
public readonly record struct BuildingType(string Name) : ITypeId<BuildingType>
{
    public static BuildingType CommandCenter { get; } = new("CommandCenter");
    public static BuildingType Barracks { get; } = new("Barracks");
    public static BuildingType ResourceDepot { get; } = new("ResourceDepot");
    public static BuildingType TechLab { get; } = new("TechLab");
    public static BuildingType GuardTower { get; } = new("GuardTower");
    public static BuildingType SiegeWorkshop { get; } = new("SiegeWorkshop");

    public static BuildingType From(string name) => new(name);

    public static implicit operator BuildingType(string name) => new(name);

    public bool Equals(BuildingType other) => TypeId.Equal(Name, other.Name);

    public override int GetHashCode() => TypeId.Hash(Name);

    public int CompareTo(BuildingType other) => TypeId.Compare(Name, other.Name);

    public override string ToString() => Name ?? "";
}

[JsonConverter(typeof(TypeIdConverter<UpgradeType>))]
public readonly record struct UpgradeType(string Name) : ITypeId<UpgradeType>
{
    public static UpgradeType Weapons1 { get; } = new("Weapons1");
    public static UpgradeType Weapons2 { get; } = new("Weapons2");
    public static UpgradeType Armor1 { get; } = new("Armor1");
    public static UpgradeType Armor2 { get; } = new("Armor2");
    public static UpgradeType Mobility1 { get; } = new("Mobility1");
    public static UpgradeType Mobility2 { get; } = new("Mobility2");
    public static UpgradeType Harvesting1 { get; } = new("Harvesting1");
    public static UpgradeType Harvesting2 { get; } = new("Harvesting2");
    public static UpgradeType SiegeEngineering { get; } = new("SiegeEngineering");

    public static UpgradeType From(string name) => new(name);

    public static implicit operator UpgradeType(string name) => new(name);

    public bool Equals(UpgradeType other) => TypeId.Equal(Name, other.Name);

    public override int GetHashCode() => TypeId.Hash(Name);

    public int CompareTo(UpgradeType other) => TypeId.Compare(Name, other.Name);

    public override string ToString() => Name ?? "";
}

internal static class TypeId
{
    public static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static int Hash(string? name) => name is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(name);

    public static int Compare(string? a, string? b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads and writes a type id as its name, including as a dictionary key.</summary>
public sealed class TypeIdConverter<T> : JsonConverter<T>
    where T : struct, ITypeId<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } name
            ? T.From(name)
            : throw new JsonException($"Expected a {typeof(T).Name} name (a string).");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { Length: > 0 } name ? T.From(name) : throw new JsonException($"Expected a {typeof(T).Name} name.");

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Name ?? "");
}
