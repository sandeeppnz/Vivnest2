import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createContext, useContext, useMemo, useRef, type ReactNode } from "react";
import { ApiError } from "./api";

// The tenant session (dashboard-redesign-plan.md D2). Before this,
// apiKey and onAuthError were threaded as props through every component
// in the tree; now the key lives in context and 401 handling lives in
// ONE place - the query/mutation caches below - instead of a catch in
// every fetch site.

const ApiKeyContext = createContext<string | null>(null);

export function useApiKey(): string {
  const key = useContext(ApiKeyContext);
  if (key === null) throw new Error("useApiKey called outside SessionProvider");
  return key;
}

interface SessionProviderProps {
  apiKey: string;
  onAuthError: () => void;
  children: ReactNode;
}

export function SessionProvider({ apiKey, onAuthError, children }: SessionProviderProps) {
  // Ref, not a memo dependency: App re-creates the callback every render,
  // and rebuilding the QueryClient would wipe the cache each time.
  const onAuthErrorRef = useRef(onAuthError);
  onAuthErrorRef.current = onAuthError;

  // One client per session key: logging out (or switching keys) drops the
  // whole cache rather than leaking one session's data into the next.
  const client = useMemo(() => {
    const handle401 = (error: unknown, meta: Record<string, unknown> | undefined) => {
      // ApiKeysAdmin's operator tier owns its own key and its own 401
      // handling (clear the operator key, show its gate) - a bad operator
      // key must not log the tenant session out.
      if (meta?.operatorTier) return;

      if (error instanceof ApiError && error.status === 401) {
        onAuthErrorRef.current();
      }
    };

    return new QueryClient({
      queryCache: new QueryCache({
        onError: (error, query) => handle401(error, query.meta),
      }),
      mutationCache: new MutationCache({
        onError: (error, _variables, _context, mutation) => handle401(error, mutation.meta),
      }),
      defaultOptions: {
        queries: {
          retry: 1,
          staleTime: 10_000,
        },
      },
    });
    // apiKey is deliberately a dependency even though the body never
    // reads it: a DIFFERENT key must get a fresh client (fresh cache),
    // or the next login would briefly see the previous session's data.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  return (
    <ApiKeyContext.Provider value={apiKey}>
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    </ApiKeyContext.Provider>
  );
}
