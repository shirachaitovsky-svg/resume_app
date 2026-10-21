using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;
using ResumeApp.Infrastructure;
using SkiaSharp;

namespace ResumeApp.Tests;

[Collection("env")]
public class ProfileOptionalFieldsTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, string Token);

    private async Task<Setup> NewSetupAsync(IReadOnlyDictionary<string, string>? limits = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(factory, json.GetProperty("inviteToken").GetString()!);
    }

    private static Dictionary<string, string> Required(string email = "dana@example.com") => new()
    {
        ["name"] = "דנה כהן",
        ["productName"] = "מוצר מגניב",
        ["productUrl"] = "https://example.com/app",
        ["email"] = email,
        ["phone"] = "050-123 4567",
    };

    private static Task<HttpResponseMessage> SubmitJson(ApiClient c, string token, object body)
        => c.PostWithCsrfAsync($"/invites/{token}/profiles", body);

    private static async Task<HttpResponseMessage> SubmitMultipart(ApiClient c, string token, Dictionary<string, string> fields,
        byte[]? photo = null, string fileName = "me.png", string photoType = "image/png")
    {
        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields) content.Add(new StringContent(v, Encoding.UTF8), k);
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(photoType);
            content.Add(file, "photo", fileName);
        }
        var request = new HttpRequestMessage(HttpMethod.Post, $"/invites/{token}/profiles") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());
        return await c.Http.SendAsync(request);
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task AssertNothingStored(AuthApiFactory factory)
    {
        Assert.Equal(0, await Db(factory, d => d.Profiles.CountAsync()));
        Assert.Equal(0, await Db(factory, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(0, await Db(factory, d => d.ParticipantPrivates.CountAsync()));
    }

    private static byte[] Image(SKEncodedImageFormat format, int width = 64, int height = 48, bool noise = false)
    {
        using var bitmap = new SKBitmap(width, height);
        if (noise)
        {
            var rnd = new Random(7);
            var px = new byte[width * height * 4];
            rnd.NextBytes(px);
            for (var i = 3; i < px.Length; i += 4) px[i] = 255;
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bitmap.GetPixels(), px.Length);
        }
        else
        {
            bitmap.Erase(new SKColor(30, 120, 200));
        }
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 90).ToArray();
    }

    private static async Task<string> ErrorFields(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errors = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        return string.Join(",", errors.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Required_only_submission_still_works_with_default_avatar_and_no_extras()
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token, Required());
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);

        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("default", rev.AvatarId);
        Assert.Null(rev.Description);
        Assert.Null(rev.Role);
        Assert.Null(rev.Workplace);
        Assert.Null(rev.IntroText);
        Assert.Null(rev.IntroAnswer1);
        Assert.Null(rev.Photo);
        Assert.Null(rev.PhotoContentType);
    }

    [Fact]
    public async Task Optional_fields_in_json_are_stored_and_boundaries_accepted()
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token, new
        {
            name = "דנה", productName = "מוצר", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567",
            description = new string('ת', 120), role = new string('ר', 100), workplace = new string('מ', 100),
            introText = new string('א', 1000), introAnswer1 = "אחת", introAnswer2 = "שתיים\nשורה", introAnswer3 = new string('ב', 1000),
            avatarId = "girl-2",
        });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);

        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal(120, rev.Description!.Length);
        Assert.Equal(100, rev.Role!.Length);
        Assert.Equal(100, rev.Workplace!.Length);
        Assert.Equal(1000, rev.IntroText!.Length);
        Assert.Equal("אחת", rev.IntroAnswer1);
        Assert.Equal("שתיים\nשורה", rev.IntroAnswer2);
        Assert.Equal(1000, rev.IntroAnswer3!.Length);
        Assert.Equal("girl-2", rev.AvatarId);
    }

    [Fact]
    public async Task Too_long_optional_fields_are_400_for_that_field()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        Task<HttpResponseMessage> With(string key, string value)
        {
            var body = Required().ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            body[key] = value;
            return SubmitJson(client, s.Token, body);
        }

        Assert.Equal("description", await ErrorFields(await With("description", new string('x', 121))));
        Assert.Equal("role", await ErrorFields(await With("role", new string('x', 101))));
        Assert.Equal("workplace", await ErrorFields(await With("workplace", new string('x', 101))));
        Assert.Equal("introText", await ErrorFields(await With("introText", new string('x', 1001))));
        Assert.Equal("introAnswer1", await ErrorFields(await With("introAnswer1", new string('x', 1001))));
        Assert.Equal("introAnswer2", await ErrorFields(await With("introAnswer2", new string('x', 1001))));
        Assert.Equal("introAnswer3", await ErrorFields(await With("introAnswer3", new string('x', 1001))));
        Assert.Equal("description", await ErrorFields(await With("description", "a\u0000b")));
        await AssertNothingStored(s.Factory);
    }

    [Theory]
    [InlineData("robot-9")]
    [InlineData("Girl-1")]
    [InlineData("../x")]
    public async Task Unknown_avatar_is_400(string avatar)
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token,
            new { name = "ד", productName = "מ", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567", avatarId = avatar });
        Assert.Equal("avatarId", await ErrorFields(r));
        await AssertNothingStored(s.Factory);
    }

    [Theory]
    [InlineData("girl-1")]
    [InlineData("girl-3")]
    [InlineData("boy-1")]
    [InlineData("boy-3")]
    public async Task Each_fixed_avatar_is_accepted(string avatar)
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token,
            new { name = "ד", productName = "מ", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567", avatarId = avatar });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.Equal(avatar, (await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync())).AvatarId);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task Valid_image_in_multipart_is_reencoded_and_stored_in_the_revision(SKEncodedImageFormat format, string mime)
    {
        var s = await NewSetupAsync();
        var fields = Required();
        fields["description"] = "תיאור";
        fields["avatarId"] = "boy-2";
        var original = Image(format);
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, fields, original, "x.bin", mime);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);

        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("תיאור", rev.Description);
        Assert.Equal("image/jpeg", rev.PhotoContentType);
        Assert.NotNull(rev.Photo);
        Assert.Equal([(byte)0xFF, (byte)0xD8], rev.Photo!.Take(2).ToArray()); // re-encoded JPEG
        Assert.NotEqual(original, rev.Photo);
        Assert.True(rev.Photo.Length <= 300 * 1024);
        Assert.Equal("default", rev.AvatarId); // an uploaded photo replaces the avatar
        using var decoded = SKBitmap.Decode(rev.Photo);
        Assert.Equal(64, decoded.Width);
    }

    [Fact]
    public async Task Multipart_without_photo_works_and_is_equivalent_to_json()
    {
        var s = await NewSetupAsync();
        var fields = Required();
        fields["role"] = "מפתחת";
        fields["introAnswer3"] = "תשובה";
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, fields);
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("מפתחת", rev.Role);
        Assert.Equal("תשובה", rev.IntroAnswer3);
        Assert.Null(rev.Photo);
    }

    [Fact]
    public async Task Large_image_is_downscaled()
    {
        var s = await NewSetupAsync();
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), Image(SKEncodedImageFormat.Png, 3000, 2000));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        using var decoded = SKBitmap.Decode(rev.Photo!);
        Assert.Equal(1024, Math.Max(decoded.Width, decoded.Height));
    }

    public static IEnumerable<object[]> BadFiles()
    {
        yield return ["svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><script>alert(1)</script></svg>"), "x.svg", "image/svg+xml"];
        yield return ["svg-as-png", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "x.png", "image/png"];
        yield return ["html-as-jpg", Encoding.UTF8.GetBytes("<html><body>hi</body></html>"), "x.jpg", "image/jpeg"];
        yield return ["gif", Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"), "x.png", "image/png"];
        yield return ["garbage", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, "x.png", "image/png"];
        yield return ["truncated-png", Image(SKEncodedImageFormat.Png, 200, 200, noise: true).Take(300).ToArray(), "x.png", "image/png"];
        yield return ["png-header-only", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "x.png", "image/png"];
    }

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task Svg_non_image_or_corrupt_file_is_400_and_nothing_is_stored(string _, byte[] bytes, string name, string mime)
    {
        var s = await NewSetupAsync();
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), bytes, name, mime);
        Assert.Equal("photo", await ErrorFields(r));
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Photo_over_300KB_is_rejected_at_the_boundary_and_nothing_is_stored()
    {
        var s = await NewSetupAsync();
        var big = Image(SKEncodedImageFormat.Png, 300, 300, noise: true);
        Assert.True(big.Length > 300 * 1024);
        Assert.True(big.Length < 400 * 1024);
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), big);
        Assert.Equal("photo", await ErrorFields(r));
        await AssertNothingStored(s.Factory);

        // The processor itself: one byte over the limit is TooLarge; exactly at the limit it is examined (and is not an image).
        Assert.Equal(PhotoOutcome.TooLarge, SkiaPhotoProcessor.Process(new byte[IPhotoProcessor.MaxUploadBytes + 1]).Outcome);
        Assert.Equal(PhotoOutcome.NotAnImage, SkiaPhotoProcessor.Process(new byte[IPhotoProcessor.MaxUploadBytes]).Outcome);
    }

    [Fact]
    public async Task Multipart_over_512KB_is_413_and_nothing_is_stored()
    {
        var s = await NewSetupAsync();
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), new byte[600 * 1024]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Json_body_limit_is_still_16KB()
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token, new
        {
            name = "ד", productName = "מ", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567",
            introText = new string('x', 17 * 1024),
        });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Extreme_dimensions_are_rejected()
    {
        var s = await NewSetupAsync();
        var wide = Image(SKEncodedImageFormat.Png, 5000, 10);
        Assert.True(wide.Length < 300 * 1024);
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), wide);
        Assert.Equal("photo", await ErrorFields(r));
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Multipart_with_bad_token_gets_the_uniform_404()
    {
        var s = await NewSetupAsync();
        var r = await SubmitMultipart(s.Factory.NewClient(), ResumeApp.Domain.Cohorts.Cohort.GenerateToken(), Required(), Image(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ---- GET /profiles/{id}/photo ----

    private static async Task<HttpResponseMessage> GetPhoto(ApiClient c, string id) => await c.Http.GetAsync($"/profiles/{id}/photo");

    private async Task<(Setup S, Guid ProfileId)> SubmittedWithPhotoAsync()
    {
        var s = await NewSetupAsync();
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), Image(SKEncodedImageFormat.Png));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var profile = await Db(s.Factory, d => d.Profiles.AsNoTracking().SingleAsync());
        return (s, profile.Id);
    }

    private static Task Approve(AuthApiFactory f)
        => Db(f, async d =>
        {
            await d.Database.ExecuteSqlRawAsync("UPDATE profile_revisions SET status = 'Approved'");
            await d.Database.ExecuteSqlRawAsync("UPDATE profiles SET approved_revision_id = (SELECT id FROM profile_revisions LIMIT 1)");
            return 0;
        });

    [Fact]
    public async Task Pending_revision_photo_is_404_and_approved_one_is_served_with_nosniff()
    {
        var (s, id) = await SubmittedWithPhotoAsync();
        var anon = s.Factory.NewClient();

        var pending = await GetPhoto(anon, id.ToString());
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        Assert.Equal("no-store", pending.Headers.CacheControl?.ToString());
        Assert.Equal("application/problem+json", pending.Content.Headers.ContentType?.MediaType);

        await Approve(s.Factory);
        var ok = await GetPhoto(anon, id.ToString());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/jpeg", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", ok.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("public", ok.Headers.CacheControl!.ToString());
        Assert.DoesNotContain("no-store", ok.Headers.CacheControl!.ToString());
        var bytes = await ok.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)0xFF, bytes[0]);
        var stored = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal(stored.Photo, bytes);
    }

    [Fact]
    public async Task Removed_unknown_malformed_and_photoless_profiles_all_get_the_same_404()
    {
        var (s, id) = await SubmittedWithPhotoAsync();
        var anon = s.Factory.NewClient();
        await Approve(s.Factory);
        await Db(s.Factory, d => d.Database.ExecuteSqlRawAsync("UPDATE profiles SET visibility = 'Removed'"));

        var shapes = new List<string>();
        foreach (var target in new[] { id.ToString(), Guid.NewGuid().ToString(), "not-a-guid", "1" })
        {
            var r = await GetPhoto(anon, target);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            shapes.Add($"{json.GetProperty("type")}|{json.GetProperty("title")}|{json.GetProperty("status")}");
        }
        Assert.Single(shapes.Distinct());

        // Approved but without a photo: still the same 404.
        var s2 = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await SubmitJson(s2.Factory.NewClient(), s2.Token, Required())).StatusCode);
        var id2 = (await Db(s2.Factory, d => d.Profiles.AsNoTracking().SingleAsync())).Id;
        await Approve(s2.Factory);
        Assert.Equal(HttpStatusCode.NotFound, (await GetPhoto(s2.Factory.NewClient(), id2.ToString())).StatusCode);
    }

    [Fact]
    public async Task Photo_endpoint_is_ip_limited_and_needs_no_turnstile()
    {
        var s = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_PHOTO_IP"] = "2" });
        var anon = s.Factory.NewClient();
        anon.Http.DefaultRequestHeaders.Remove("X-Turnstile-Token");
        var id = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.NotFound, (await GetPhoto(anon, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetPhoto(anon, id)).StatusCode);
        var limited = await GetPhoto(anon, id);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(0, s.Factory.Turnstile.Calls);
    }

    [Fact]
    public void Transparent_region_is_flattened_to_white()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor(200, 0, 0) })
            canvas.DrawRect(0, 0, 20, 40, paint);
        using var png = SKImage.FromBitmap(bitmap);

        var result = SkiaPhotoProcessor.Process(png.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        Assert.Equal(PhotoOutcome.Ok, result.Outcome);
        using var decoded = SKBitmap.Decode(result.Bytes!);
        var transparent = decoded.GetPixel(32, 20);
        Assert.True(transparent.Red >= 245 && transparent.Green >= 245 && transparent.Blue >= 245, transparent.ToString());
        Assert.True(decoded.GetPixel(5, 20).Red > 150 && decoded.GetPixel(5, 20).Green < 100);
    }

    /// <summary>A 64x32 JPEG (left half red, right half blue) with an EXIF APP1 block carrying the given orientation.</summary>
    private static byte[] JpegWithOrientation(ushort orientation)
    {
        using var bitmap = new SKBitmap(64, 32);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            paint.Color = new SKColor(255, 0, 0);
            canvas.DrawRect(0, 0, 32, 32, paint);
            paint.Color = new SKColor(0, 0, 255);
            canvas.DrawRect(32, 0, 32, 32, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();

        var tiff = new List<byte> { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0,
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0, 0, 0, 0, 0, 0 };
        var payload = new byte[] { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 }.Concat(tiff).ToArray();
        var length = payload.Length + 2;
        var app1 = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF) }.Concat(payload).ToArray();
        return jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray();
    }

    [Fact]
    public void Exif_orientation_is_applied_and_the_exif_block_is_dropped()
    {
        var input = JpegWithOrientation(6); // RightTop: rotate 90 degrees clockwise for display
        using (var check = SKCodec.Create(new MemoryStream(input)))
            Assert.Equal(SKEncodedOrigin.RightTop, check!.EncodedOrigin);

        var result = SkiaPhotoProcessor.Process(input);
        Assert.Equal(PhotoOutcome.Ok, result.Outcome);
        using var decoded = SKBitmap.Decode(result.Bytes!);
        Assert.Equal(32, decoded.Width);
        Assert.Equal(64, decoded.Height);
        var top = decoded.GetPixel(16, 8);    // the original left half (red) ends up on top
        var bottom = decoded.GetPixel(16, 56);
        Assert.True(top.Red > 180 && top.Blue < 80, top.ToString());
        Assert.True(bottom.Blue > 180 && bottom.Red < 80, bottom.ToString());

        for (var i = 0; i < result.Bytes!.Length - 1; i++)
            Assert.False(result.Bytes[i] == 0xFF && result.Bytes[i + 1] == 0xE1, "APP1 marker found in the output");
    }

    [Fact]
    public async Task Pixels_over_12_megapixels_are_rejected_even_when_each_side_is_allowed()
    {
        var s = await NewSetupAsync();
        var big = Image(SKEncodedImageFormat.Png, 4000, 4000);
        Assert.True(big.Length < 300 * 1024, big.Length.ToString());
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), big);
        Assert.Equal("photo", await ErrorFields(r));
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Concurrency_gate_returns_503_with_retry_hint_when_full()
    {
        var s = await NewSetupAsync();
        var gate = typeof(SkiaPhotoProcessor).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var processor = (SkiaPhotoProcessor)s.Factory.Services.GetRequiredService<IPhotoProcessor>();
        var semaphore = (SemaphoreSlim)gate.GetValue(processor)!;
        for (var i = 0; i < SkiaPhotoProcessor.MaxConcurrency; i++) await semaphore.WaitAsync();
        try
        {
            var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, Required(), Image(SKEncodedImageFormat.Png));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.True(r.Headers.Contains("Retry-After"));
            await AssertNothingStored(s.Factory);
        }
        finally
        {
            semaphore.Release(SkiaPhotoProcessor.MaxConcurrency);
        }
    }

    // ---- multipart robustness ----

    private static async Task<HttpResponseMessage> SendRawMultipart(ApiClient c, string token, string boundaryHeader, byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundaryHeader);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/invites/{token}/profiles") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());
        return await c.Http.SendAsync(request);
    }

    private static byte[] Parts(string boundary, IEnumerable<(string Name, string Value)> parts, bool terminate = true)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in parts)
            sb.Append($"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
        if (terminate) sb.Append($"--{boundary}--\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public async Task Garbled_or_truncated_multipart_is_400_and_never_500()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var fields = Required().Select(kv => (kv.Key, kv.Value)).ToList();

        var truncated = Parts("abc", fields, terminate: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "abc", truncated.Take(truncated.Length - 20).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "abc", Encoding.UTF8.GetBytes("not multipart at all"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "abc", Encoding.UTF8.GetBytes("--abc\r\nbroken header line\r\n\r\nx\r\n--abc--\r\n"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "wrong-boundary", Parts("abc", fields))).StatusCode);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task More_than_30_parts_is_400()
    {
        var s = await NewSetupAsync();
        var parts = Required().Select(kv => (kv.Key, kv.Value)).Concat(Enumerable.Range(0, 30).Select(i => ($"extra{i}", "x"))).ToList();
        var r = await SendRawMultipart(s.Factory.NewClient(), s.Token, "abc", Parts("abc", parts));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Duplicate_field_or_duplicate_photo_part_is_400()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var dupField = Required().Select(kv => (kv.Key, kv.Value)).Append(("role", "a")).Append(("Role", "b")).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "abc", Parts("abc", dupField))).StatusCode);

        var photo = Image(SKEncodedImageFormat.Png);
        var body = new List<byte>(Parts("abc", Required().Select(kv => (kv.Key, kv.Value)), terminate: false));
        for (var i = 0; i < 2; i++)
        {
            body.AddRange(Encoding.UTF8.GetBytes("--abc\r\nContent-Disposition: form-data; name=\"photo\"; filename=\"a.png\"\r\nContent-Type: image/png\r\n\r\n"));
            body.AddRange(photo);
            body.AddRange(Encoding.UTF8.GetBytes("\r\n"));
        }
        body.AddRange(Encoding.UTF8.GetBytes("--abc--\r\n"));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawMultipart(client, s.Token, "abc", body.ToArray())).StatusCode);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Empty_photo_part_means_no_photo_and_field_names_are_case_insensitive()
    {
        var s = await NewSetupAsync();
        var parts = Required().Select(kv => (kv.Key, kv.Value)).Append(("ROLE", "מפתחת")).Append(("AvatarId", "default")).ToList();
        var body = new List<byte>(Parts("abc", parts, terminate: false));
        body.AddRange(Encoding.UTF8.GetBytes("--abc\r\nContent-Disposition: form-data; name=\"photo\"; filename=\"\"\r\nContent-Type: application/octet-stream\r\n\r\n\r\n--abc--\r\n"));
        var r = await SendRawMultipart(s.Factory.NewClient(), s.Token, "abc", body.ToArray());
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var rev = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("מפתחת", rev.Role);
        Assert.Null(rev.Photo);
        Assert.Equal("default", rev.AvatarId);
    }

    [Fact]
    public async Task Invalid_avatar_is_rejected_even_when_a_photo_is_present()
    {
        var s = await NewSetupAsync();
        var fields = Required();
        fields["avatarId"] = "robot-9";
        var r = await SubmitMultipart(s.Factory.NewClient(), s.Token, fields, Image(SKEncodedImageFormat.Png));
        Assert.Equal("avatarId", await ErrorFields(r));
        await AssertNothingStored(s.Factory);
    }

    // ---- hidden / unusual characters ----

    [Fact]
    public async Task Hidden_characters_are_rejected_and_tabs_are_allowed_only_in_multiline_fields()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        Task<HttpResponseMessage> With(string key, string value)
        {
            var body = Required().ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            body[key] = value;
            return SubmitJson(client, s.Token, body);
        }

        foreach (var bad in new[] { "a\u2028b", "a\u2029b", "a\u200Bb", "a\u200Fb", "a\u202Ab", "a\u202Eb", "a\u2066b", "a\u2069b", "a\uFEFFb" })
        {
            foreach (var field in new[] { "description", "role", "workplace", "introText", "introAnswer1", "introAnswer2", "introAnswer3" })
                Assert.Equal(field, await ErrorFields(await With(field, bad)));
        }
        Assert.Equal("description", await ErrorFields(await With("description", "a\tb")));
        Assert.Equal("role", await ErrorFields(await With("role", "a\nb")));
        await AssertNothingStored(s.Factory);

        Assert.Equal(HttpStatusCode.Accepted, (await With("introText", "שורה\tעם טאב\nושורה שנייה")).StatusCode);
        Assert.Equal("שורה\tעם טאב\nושורה שנייה", (await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync())).IntroText);
    }

    [Fact]
    public async Task Avatar_default_is_accepted_as_absent()
    {
        var s = await NewSetupAsync();
        var r = await SubmitJson(s.Factory.NewClient(), s.Token,
            new { name = "ד", productName = "מ", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567", avatarId = "default" });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        Assert.Equal("default", (await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().SingleAsync())).AvatarId);
    }

    // ---- approved-revision selection ----

    [Fact]
    public async Task Only_the_revision_named_by_approved_revision_id_is_served()
    {
        var s = await NewSetupAsync();
        var anon = s.Factory.NewClient();
        await SubmitMultipart(anon, s.Token, Required("one@example.com"), Image(SKEncodedImageFormat.Png, 20, 20));
        await SubmitJson(anon, s.Token, Required("two@example.com"));
        var profiles = await Db(s.Factory, d => d.Profiles.AsNoTracking().ToListAsync());
        var revisions = await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().ToListAsync());
        var withPhoto = revisions.Single(r => r.Photo is not null);
        var noPhoto = revisions.Single(r => r.Photo is null);
        var photoProfile = profiles.Single(p => p.Id == withPhoto.ProfileId);
        var plainProfile = profiles.Single(p => p.Id == noPhoto.ProfileId);

        // A second revision for the photo profile (the first is superseded), so one profile has two revisions.
        var second = ProfileRevision.CreatePending(photoProfile.Id, "x", "y", "https://z.example", DateTime.UtcNow);
        await Db(s.Factory, async d =>
        {
            await d.Database.ExecuteSqlRawAsync("UPDATE profile_revisions SET status = 'Superseded' WHERE id = {0}", withPhoto.Id);
            d.ProfileRevisions.Add(second);
            await d.SaveChangesAsync();
            return 0;
        });

        // Approved revision (no photo) named; the other revision of the same profile has a photo: 404.
        await Db(s.Factory, async d =>
        {
            await d.Database.ExecuteSqlRawAsync("UPDATE profile_revisions SET status = 'Approved' WHERE id = {0}", second.Id);
            await d.Database.ExecuteSqlRawAsync("UPDATE profiles SET approved_revision_id = {0} WHERE id = {1}", second.Id, photoProfile.Id);
            return 0;
        });
        Assert.Equal(HttpStatusCode.NotFound, (await GetPhoto(anon, photoProfile.Id.ToString())).StatusCode);

        // A profile whose approved_revision_id points at another profile's revision: 404.
        await Db(s.Factory, async d =>
        {
            await d.Database.ExecuteSqlRawAsync("UPDATE profiles SET approved_revision_id = {0} WHERE id = {1}", withPhoto.Id, plainProfile.Id);
            return 0;
        });
        Assert.Equal(HttpStatusCode.NotFound, (await GetPhoto(anon, plainProfile.Id.ToString())).StatusCode);

        // Pointing the photo profile at its own approved revision that has a photo: 200 with exactly that revision's bytes.
        await Db(s.Factory, async d =>
        {
            await d.Database.ExecuteSqlRawAsync("UPDATE profile_revisions SET status = 'Superseded' WHERE id = {0}", second.Id);
            await d.Database.ExecuteSqlRawAsync("UPDATE profile_revisions SET status = 'Approved' WHERE id = {0}", withPhoto.Id);
            await d.Database.ExecuteSqlRawAsync("UPDATE profiles SET approved_revision_id = {0} WHERE id = {1}", withPhoto.Id, photoProfile.Id);
            return 0;
        });
        var ok = await GetPhoto(anon, photoProfile.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(withPhoto.Photo, await ok.Content.ReadAsByteArrayAsync());
    }
}
