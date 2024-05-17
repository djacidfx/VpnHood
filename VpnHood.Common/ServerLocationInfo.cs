﻿using System.Globalization;

namespace VpnHood.Common;

public class ServerLocationInfo : IComparable<ServerLocationInfo>
{
    // do not use auto property because NSwag generate incorrect code
    public static readonly ServerLocationInfo Auto = new() { CountryCode = "*", RegionName = "*" };
    public required string CountryCode { get; init; }
    public required string RegionName { get; init; }
    public string ServerLocation => $"{CountryCode}/{RegionName}";
    public string CountryName => GetCountryName(CountryCode);
    
    public int CompareTo(ServerLocationInfo other)
    {
        var countryComparison = string.Compare(CountryCode, other.CountryCode, StringComparison.OrdinalIgnoreCase);
        return countryComparison != 0 ? countryComparison : string.Compare(RegionName, other.RegionName, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return ServerLocation == (obj as ServerLocationInfo)?.ServerLocation;
    }


    public override string ToString()
    {
        return ServerLocation;
    }

    public override int GetHashCode()
    {
        return ServerLocation.GetHashCode();
    }

    public static ServerLocationInfo Parse(string value)
    {
        var parts = value.Split('/');
        var ret = new ServerLocationInfo
        {
            CountryCode = ParseLocationPart(parts, 0),
            RegionName = ParseLocationPart(parts, 1)
        };
        return ret;
    }

    public bool IsMatch(ServerLocationInfo serverLocationInfo)
    {
        return
            MatchLocationPart(CountryCode, serverLocationInfo.CountryCode) &&
            MatchLocationPart(RegionName, serverLocationInfo.RegionName);
    }

    private static string ParseLocationPart(IReadOnlyList<string> parts, int index)
    {
        return parts.Count <= index || string.IsNullOrWhiteSpace(parts[index]) ? "*" : parts[index].Trim();
    }

    private static bool MatchLocationPart(string serverPart, string requestPart)
    {
        return requestPart == "*" || requestPart.Equals(serverPart, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCountryName(string countryCode)
    {
        try
        {
            if (countryCode == "*") return "(auto)";
            var regionInfo = new RegionInfo(countryCode);
            return regionInfo.EnglishName;
        }
        catch (Exception)
        {
            return countryCode;
        }
    }
   
}