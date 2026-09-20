using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Vortex.Rooms.Camera.Render;

/// <summary>
/// Which asset library an avatar part lives in.
/// </summary>
/// <remarks>
/// <para>
/// An avatar reaches the camera already taken apart: the payload names one sprite per body part, and
/// a part names itself rather than its library — <c>h_std_bd_1_2_0</c> is size <c>h</c>, action
/// <c>std</c>, part type <c>bd</c>, part id <c>1</c>, then direction and frame. The client knows
/// <c>hh_human_body</c> holds that because it read <c>figuremap.xml</c>, and so does this.
/// </para>
/// <para>
/// A part id belongs to one library per type and not per id alone: id 1 is a body under <c>bd</c>
/// and something else entirely under <c>ch</c>. The key is both.
/// </para>
/// </remarks>
internal sealed class FiguremapIndex
{
    private readonly Dictionary<string, string> _libraries;

    private FiguremapIndex(Dictionary<string, string> libraries) => _libraries = libraries;

    public static FiguremapIndex Empty { get; } = new([]);

    public int Count => _libraries.Count;

    /// <summary>Reads the client's own figuremap. Never throws: a malformed one is an empty index.</summary>
    public static FiguremapIndex Read(string xml)
    {
        Dictionary<string, string> libraries = new(StringComparer.Ordinal);

        try
        {
            XmlReaderSettings settings = new()
            {
                // The file comes over HTTP from a host in configuration. Entity resolution is off
                // for the ordinary reason: nothing here needs it and it reads files.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true,
            };

            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, settings);

            string? library = null;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name == "lib")
                {
                    library = reader.GetAttribute("id");
                    continue;
                }

                if (reader.Name != "part" || library is null)
                {
                    continue;
                }

                string? type = reader.GetAttribute("type");
                string? id = reader.GetAttribute("id");

                if (type is { Length: > 0 } && id is { Length: > 0 })
                {
                    // First writer wins. A part listed twice is the figuremap's business, and taking
                    // the first keeps this deterministic rather than order-dependent in a way that
                    // would move with the file.
                    libraries.TryAdd(Key(type, id), library);
                }
            }
        }
        catch (XmlException)
        {
            return Empty;
        }

        return new FiguremapIndex(libraries);
    }

    /// <summary>
    /// The library for a sprite name, if it looks like an avatar part at all.
    /// </summary>
    /// <remarks>
    /// The shape is <c>size_action_type_id_direction_frame</c>, and the action may itself carry
    /// underscores — <c>h_std_bd_1_2_0</c> against something like <c>h_wlk_bd_1_2_3</c> — so the
    /// type and id are taken from the right, counting back past direction and frame, rather than
    /// from the left.
    /// </remarks>
    public bool TryLibrary(string assetName, out string? library)
    {
        library = null;

        string[] parts = assetName.Split('_');

        // size, action, type, id, direction, frame.
        if (parts.Length < 6)
        {
            return false;
        }

        string type = parts[^4];
        string id = parts[^3];

        if (_libraries.TryGetValue(Key(type, id), out library))
        {
            return true;
        }

        // The figuremap is not complete, and the client copes, so this has to as well. A real photo
        // asked for h_std_lc_2065_2_0, h_std_rc_2065_2_0 and h_std_hrb_501_2_0, and the map declares
        // none of the three: jacket_M_general lists cc, lc and rc for id 2064 and then only cc for
        // 2065, and hh_human_hair lists hrb for id 1 alone out of forty hair styles. The sleeves and
        // the back of the hair are in those libraries all the same.
        return Companions.TryGetValue(type, out string? primary)
            && _libraries.TryGetValue(Key(primary, id), out library);
    }

    /// <summary>
    /// The part a sub-part is packed with, counted off the figuremap rather than remembered.
    /// </summary>
    /// <remarks>
    /// Every (library, id) in the file declares a set of types; counting which types travel together
    /// gives these pairs and their weight — <c>ls</c> and <c>rs</c> sit with <c>ch</c> 527 times,
    /// <c>lc</c> and <c>rc</c> with <c>cc</c> 301 times, <c>hrb</c> with <c>hr</c> 231 times.
    /// <para>
    /// An id alone will not do, and that is the trap this avoids: id 501 is <c>hh_human_hair</c>
    /// under <c>hr</c> and <c>hh_human_shirt</c> under <c>ch</c>. The type has to come along.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> Companions = new(StringComparer.Ordinal)
    {
        ["lc"] = "cc",
        ["rc"] = "cc",
        ["ls"] = "ch",
        ["rs"] = "ch",
        ["hrb"] = "hr",
        ["lh"] = "bd",
        ["rh"] = "bd",
    };

    private static string Key(string type, string id) => $"{type}:{id}";
}
