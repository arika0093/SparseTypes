namespace SparseTypes.OpenApi.AspNetCore.Tests.SchemaNames
{
    public sealed class Invoice;

    public sealed class Invoice_DEADBEEF;

    public sealed class __InternalLedger;

    public static class Holder
    {
        public sealed class Line_0B56D634;
    }

    public static class __Internal_0B56D634
    {
        public sealed class Change1_0B56D634;

        public sealed class Core_0B56D634;

        public sealed class Plain_0B56D634Extra;
    }
}

namespace SparseTypes.OpenApi.AspNetCore.Tests
{
    public sealed class SparsePayloadSchemaNamesTests
    {
        private const string Prefix = "SparseTypes_OpenApi_AspNetCore_Tests_SchemaNames_";

        [Test]
        public void OrdinaryUserTypesKeepHexSuffixesAndInternalPrefixes()
        {
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.Invoice)).ShouldBe(Prefix + "Invoice");
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.Invoice_DEADBEEF)).ShouldBe(Prefix + "Invoice_DEADBEEF");
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.__InternalLedger)).ShouldBe(Prefix + "__InternalLedger");
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.Holder.Line_0B56D634)).ShouldBe(Prefix + "Holder_Line_0B56D634");
        }

        [Test]
        public void GeneratedPlumbingTypesKeepTheirStableNames()
        {
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.__Internal_0B56D634.Change1_0B56D634)).ShouldBe(Prefix + "Change1");
            SparsePayloadSchemaNames.GetName(typeof(SchemaNames.__Internal_0B56D634.Core_0B56D634)).ShouldBe(Prefix + "Core");
            SparsePayloadSchemaNames
                .GetName(typeof(SchemaNames.__Internal_0B56D634.Plain_0B56D634Extra))
                .ShouldBe(Prefix + "Plain_0B56D634Extra");
        }
    }
}
