// Which admin screen a /admin/:screen URL names.
export type AdminView =
  | "capabilities"
  | "deviceTypes"
  | "devices"
  | "agents"
  | "machines"
  | "agentInstallations"
  | "apiKeys";

// The URL scheme (dashboard-redesign-plan.md D1):
//
//   /                        Overview
//   /devices?status=Error    device list, filter in the query string
//   /devices/:id/:tab?       device detail (overview|activity|configuration)
//   /agents, /agents/:id/:tab?, /events - same shapes
//   /admin/:screen           the admin screens, kebab-case slugs
//
// Everything here exists so App.tsx and the detail pages agree on one
// vocabulary instead of each re-deriving it.

export type DetailTab = "overview" | "activity" | "configuration";

export function toDetailTab(raw: string | undefined): DetailTab {
  return raw === "activity" || raw === "configuration" ? raw : "overview";
}

// Kebab-case in the URL, the existing AdminView ids in code.
const ADMIN_BY_SLUG: Record<string, AdminView> = {
  "capabilities": "capabilities",
  "device-types": "deviceTypes",
  "devices": "devices",
  "agents": "agents",
  "machines": "machines",
  "agent-installations": "agentInstallations",
  "api-keys": "apiKeys",
};

export const SLUG_BY_ADMIN: Record<AdminView, string> = Object.fromEntries(
  Object.entries(ADMIN_BY_SLUG).map(([slug, view]) => [view, slug]),
) as Record<AdminView, string>;

export function toAdminView(slug: string | undefined): AdminView | null {
  return (slug && ADMIN_BY_SLUG[slug]) || null;
}

export function statusFilterFrom(search: string): string | null {
  return new URLSearchParams(search).get("status");
}

export function listPath(base: "/devices" | "/agents", statusFilter: string | null): string {
  return statusFilter ? `${base}?status=${encodeURIComponent(statusFilter)}` : base;
}
