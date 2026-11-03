namespace Doka.EntityFrameworkCore.NestedSet.Features.ManagedSave;

internal static partial class NestedSetSaveChanges
{
    /// <summary>Authorizes only detached members of one completely validated import during its bounded saves.</summary>
    /// <param name="context">The context owning the complete subtree or forest transaction.</param>
    /// <param name="inputs">The lazy input-reference and exact planned-FK proof.</param>
    /// <returns>A scope that removes all input authorization on success, failure or cancellation.</returns>
    internal static IDisposable EnterImportInputs(
        DbContext context,
        NestedSetImportInputs inputs
    )
    {
        var state = s_contexts.GetValue(context, static _ => new SaveState());

        return new ImportInputScope(state, inputs);
    }

    /// <summary>Restores an outer import without leaving a completed plan attached to a pooled context.</summary>
    private sealed class ImportInputScope : IDisposable
    {
        private readonly SaveState _state;
        private readonly NestedSetImportInputs _inputs;
        private readonly NestedSetImportInputs? _previous;
        private bool _disposed;

        /// <summary>Installs one operation-scoped proof without changing the active batch write set.</summary>
        internal ImportInputScope(
            SaveState state,
            NestedSetImportInputs inputs
        )
        {
            _state = state;
            _inputs = inputs;
            _previous = state.ImportInputs;
            state.ImportInputs = inputs;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                _inputs.ClearCollectionScratch();
                _state.ImportInputs = _previous;
                _disposed = true;
            }
        }
    }
}
