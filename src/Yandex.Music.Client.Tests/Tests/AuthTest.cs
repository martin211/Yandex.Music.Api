using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions.Ordering;

namespace Yandex.Music.Client.Tests.Tests;

[Collection("Yandex Test Harness"), Order(1)]
[TestBeforeAfter]
public class AuthTest : YandexTest
{
    public AuthTest(YandexTestHarness fixture, ITestOutputHelper output) : base(fixture, output)
    {
    }

    [Fact]
    [Order(0)]
    public void GetQrLink_True()
    {
        var authSession = Fixture.Client.CreateAuthSession("evgeny.bogdan84");
        var qrLink = Fixture.Client.GetAuthQRLink();

        qrLink.Should().NotBeNull();
    }
}