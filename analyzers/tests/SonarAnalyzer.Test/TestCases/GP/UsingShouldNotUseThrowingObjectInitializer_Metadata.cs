namespace Tests.Diagnostics
{
    // Every member here comes from a referenced assembly, so there is no declaration syntax to read an accessor or a
    // modifier from - 'init' and 'required' have to be recognized through metadata, or the fix would move them out of
    // the initializer, which is the only place they can be assigned at all.
    public class MetadataInitializerMembers
    {
        public void InitOnlyFromMetadataHasNoFix()
        {
            using var resource = new Library.InitOnlyDisposable { Value = Compute() }; // Noncompliant
        }

        public void RequiredFromMetadataHasNoFix()
        {
            using var resource = new Library.RequiredDisposable { Value = Compute() }; // Noncompliant
        }

        public void RequiredFieldFromMetadataHasNoFix()
        {
            using var resource = new Library.RequiredFieldDisposable { Value = Compute() }; // Noncompliant
        }

        // An ordinary settable property from the same assembly is still moved out, so the exclusion above is about
        // 'init'/'required' rather than about the member merely coming from metadata.
        public void PlainMetadataMemberIsFixed()
        {
            using var resource = new Library.PlainDisposable { Value = Compute() }; // Noncompliant
        }

        private static int Compute() => 42;
    }
}
