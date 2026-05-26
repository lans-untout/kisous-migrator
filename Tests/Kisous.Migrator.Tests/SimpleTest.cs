using NUnit.Framework;
namespace TmpTests {
    [TestFixture]
    public class SimpleTest {
        [Test]
        public void OneIsOne() {
            Assert.That(1, Is.EqualTo(1));
        }
    }
}
