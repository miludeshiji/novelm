using System.Globalization;
using NovelM_App.Presentation.Publishing;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class PublishingPageTests
{
    [TestMethod]
    public void ToNullableInt64_UnsetOrInvalidValue_ReturnsNull()
    {
        Assert.IsNull(PublishingPage.ToNullableInt64(double.NaN));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.PositiveInfinity));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.NegativeInfinity));
        Assert.IsNull(PublishingPage.ToNullableInt64(-1));
    }

    [TestMethod]
    public void ToNullableInt64_ValidValue_UsesExistingMidpointToEvenRounding()
    {
        Assert.AreEqual(0L, PublishingPage.ToNullableInt64(0));
        Assert.AreEqual(42L, PublishingPage.ToNullableInt64(42.4));
        Assert.AreEqual(42L, PublishingPage.ToNullableInt64(42.5));
        Assert.AreEqual(43L, PublishingPage.ToNullableInt64(42.6));
    }

    [TestMethod]
    public void ToNullableInt64_LongMaximumParsedAsDouble_ReturnsNullWithoutThrowing()
    {
        var parsedLongMaximum = double.Parse(
            long.MaxValue.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        var twoToThePowerOf63 = Math.Pow(2, 63);

        Assert.AreEqual(twoToThePowerOf63, parsedLongMaximum);
        Assert.IsNull(PublishingPage.ToNullableInt64(parsedLongMaximum));
        Assert.IsNull(PublishingPage.ToNullableInt64(twoToThePowerOf63));
        Assert.IsNull(PublishingPage.ToNullableInt64(double.MaxValue));
    }
}
