interface ErrorStateProps {
  message: string;
  onRetry: () => void;
}

// A full-screen (or full-list) load failure with a way back. Every screen
// used to render a bare <p className="error"> and stop - after one
// transient failure (a flaky load, a failed delete) the whole screen
// became a dead end that only navigating away and back could recover.
export function ErrorState({ message, onRetry }: ErrorStateProps) {
  return (
    <div className="error-state">
      <p className="error">{message}</p>
      <button type="button" className="confirm-dialog-cancel" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}
