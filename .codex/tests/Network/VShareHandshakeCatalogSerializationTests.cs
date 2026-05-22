using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers JSON-backed VShare catalog packet serialization.
/// </summary>
public sealed class VShareHandshakeCatalogSerializationTests
{
    /// <summary>
    /// Ensures the server-advertised catalog survives the non-blittable VNetwork JSON round trip.
    /// </summary>
    [Fact]
    public void SharedClientModPreview_RoundTripsEntriesThroughSerialization()
    {
        var preview = new Transference.SharedClientModPreview(
            new[]
            {
                new Transference.SharedClientModPreviewEntry(
                    "Eclipse",
                    "mfoltz/Eclipse",
                    "v1.3.17-pre",
                    "Eclipse.dll",
                    false,
                    true),
                new Transference.SharedClientModPreviewEntry(
                    "RetroCamera",
                    "mfoltz/RetroCamera",
                    "v1.5.4",
                    "RetroCamera.dll",
                    false,
                    true)
            });

        byte[] payload = Serialization.GetPacker(typeof(Transference.SharedClientModPreview))(preview);

        var roundTrip = (Transference.SharedClientModPreview)Serialization
            .GetUnpacker(typeof(Transference.SharedClientModPreview))(payload);

        Assert.Collection(
            roundTrip.Entries,
            entry =>
            {
                Assert.Equal("Eclipse", entry.DisplayName);
                Assert.Equal("mfoltz/Eclipse", entry.GitHubRepo);
                Assert.Equal("v1.3.17-pre", entry.GitHubTag);
                Assert.Equal("Eclipse.dll", entry.FileName);
                Assert.True(entry.Hotload);
            },
            entry =>
            {
                Assert.Equal("RetroCamera", entry.DisplayName);
                Assert.Equal("mfoltz/RetroCamera", entry.GitHubRepo);
                Assert.Equal("v1.5.4", entry.GitHubTag);
                Assert.Equal("RetroCamera.dll", entry.FileName);
                Assert.True(entry.Hotload);
            });
    }
}
