namespace Mova.Infrastructure.Common;

public static class DefaultProfilePictures
{
    public static readonly string[] Urls =
    [
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425976/profile2.png",
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425962/profile1.png",
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425878/profile7.png",
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425876/profile4.png",
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425867/profile5.png",
        "https://res.cloudinary.com/et0r3out/image/upload/v1789425858/profile3.png",
    ];

    public static string PickRandom()
    {
        var index = Random.Shared.Next(Urls.Length);
        return Urls[index];
    }
}