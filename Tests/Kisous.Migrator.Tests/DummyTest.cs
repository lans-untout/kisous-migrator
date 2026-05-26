using NUnit.Framework;
namespace Kisous.Migrator.Tests;
[TestFixture]
public class DummyTest {
    [Test]
    public void Ok() {
        var isOk = true;
        Assert.That(isOk, Is.True);
    }
}
