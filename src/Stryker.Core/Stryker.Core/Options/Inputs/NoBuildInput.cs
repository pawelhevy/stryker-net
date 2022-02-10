namespace Stryker.Core.Options.Inputs
{
    public class NoBuildInput : Input<bool?>
    {
        public override bool? Default => false;

        protected override string Description => "Prevents to build the test project before running mutation tests. Make sure they are builded before running Stryker.";

        public NoBuildInput() { }

        public bool Validate() => SuppliedInput ?? Default.Value;
    }
}
