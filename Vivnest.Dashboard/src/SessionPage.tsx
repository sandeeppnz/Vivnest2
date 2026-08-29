import { API_BASE_URL } from "./api";
import { ErrorState } from "./ErrorState";
import { getStoredMode, hasPin } from "./mode";
import { useWhoAmIRaw } from "./queries";

// The session inspector (Settings -> Debug): what this browser is actually
// talking to and as whom - the raw /whoami response, the resolved API
// target, and the client-side state that isn't visible anywhere else.
// Deliberately shows key PRESENCE, never values.
export function SessionPage() {
  const whoAmIQuery = useWhoAmIRaw();

  if (whoAmIQuery.isError) {
    return <ErrorState message={whoAmIQuery.error.message} onRetry={() => whoAmIQuery.refetch()} />;
  }

  const client = {
    apiBaseUrl: API_BASE_URL,
    buildMode: import.meta.env.MODE,
    dashboardMode: getStoredMode(),
    homePinSet: hasPin(),
    tenantKeyStored: localStorage.getItem("vivnest.apiKey") !== null,
    operatorKeyStored: sessionStorage.getItem("vivnest.operatorKey") !== null,
    alertsSeenUtc: localStorage.getItem("vivnest.alertsSeenUtc"),
  };

  return (
    <>
      <h3 className="section-heading">/whoami</h3>
      {!whoAmIQuery.data ? (
        <p>Loading...</p>
      ) : (
        <pre className="form-json-preview">{JSON.stringify(whoAmIQuery.data, null, 2)}</pre>
      )}

      <h3 className="section-heading">Client</h3>
      <pre className="form-json-preview">{JSON.stringify(client, null, 2)}</pre>
    </>
  );
}
