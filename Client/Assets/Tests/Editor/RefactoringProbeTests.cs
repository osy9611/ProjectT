using NUnit.Framework;

public class RefactoringProbeTests
{
    [SetUp]
    public void SetUp()
    {
        RefactoringProbe.CreateFixtures();
    }

    [TearDown]
    public void TearDown()
    {
        RefactoringProbe.DeleteFixtures();
    }

    [Test]
    public void UISoundToolPrefabEdits()
    {
        RefactoringProbe.PrefabSoundEdits();
        Assert.That(RefactoringProbe.Checks, Is.EqualTo(14));
    }
}
