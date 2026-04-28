namespace Ops.Plugins.Registration
{
    public sealed class RegistrationSyncRunner
    {
        private readonly PluginAssemblyInspector _inspector;
        private readonly DataverseRegistrationRepository _repository;
        private readonly RegistrationComparer _comparer;
        private readonly RegistrationConsoleReporter _reporter;

        public RegistrationSyncRunner(PluginAssemblyInspector inspector, DataverseRegistrationRepository repository, RegistrationComparer comparer, RegistrationConsoleReporter reporter)
        {
            _inspector = inspector;
            _repository = repository;
            _comparer = comparer;
            _reporter = reporter;
        }

        public int Run(RegistrationOptions options)
        {
            var desired = _inspector.Inspect(options.AssemblyPath);
            var actual = _repository.Load(desired, options);
            if (options.PushAssembly)
            {
                _repository.PushAssembly(actual.Assembly.Id, options.AssemblyPath);
                actual = _repository.Load(desired, options);
            }

            var plan = _comparer.Compare(desired, actual, options);

            if (options.Apply && plan.Errors == 0)
                _repository.Apply(plan);

            _reporter.Write(options, desired, actual, plan);
            return plan.Errors == 0 ? 0 : 1;
        }
    }
}
