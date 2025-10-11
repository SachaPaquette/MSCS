using System;

namespace MSCS.Sources
{
    public sealed class GenericMadaraSource : MadaraSourceBase
    {
        public GenericMadaraSource(Uri baseUri) : base(new MadaraSourceSettings(baseUri))
        {
        }

        public GenericMadaraSource(MadaraSourceSettings settings) : base(settings)
        {
        }
    }
}