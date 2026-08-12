import { useEffect } from "react";
import { CloseIcon } from "./icons";

interface AdminDrawerProps {
  open: boolean;
  onClose: () => void;
  onSelectCapabilities: () => void;
  onSelectDeviceTypes: () => void;
  onSelectDevices: () => void;
  onSelectAgents: () => void;
  onSelectApiKeys: () => void;
}

// Slide-out panel for the Admin section (Capabilities, Device Types,
// Devices, Agents today; Services/Automations once those master lists
// exist - see decision-log.md ADR-042/043/047/048). Same escape-key/
// overlay-click-to-close pattern as ConfirmDialog, left-anchored instead
// of centered since this is a nav drawer, not a confirmation. API Keys
// is set apart by a divider - it's the one item here backed by the
// operator-tier host key, not this tenant's own x-api-key, and owns its
// own login gate (ApiKeysAdmin.tsx/OperatorKeyGate.tsx).
export function AdminDrawer({
  open,
  onClose,
  onSelectCapabilities,
  onSelectDeviceTypes,
  onSelectDevices,
  onSelectAgents,
  onSelectApiKeys,
}: AdminDrawerProps) {
  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="admin-drawer-overlay" onClick={onClose}>
      <div
        className="admin-drawer"
        role="dialog"
        aria-modal="true"
        aria-label="Admin menu"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="admin-drawer-header">
          <span>Admin</span>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close menu">
            <CloseIcon />
          </button>
        </div>
        <button type="button" className="admin-drawer-item" onClick={onSelectCapabilities}>
          Capabilities
        </button>
        <button type="button" className="admin-drawer-item" onClick={onSelectDeviceTypes}>
          Device Types
        </button>
        <button type="button" className="admin-drawer-item" onClick={onSelectDevices}>
          Devices
        </button>
        <button type="button" className="admin-drawer-item" onClick={onSelectAgents}>
          Agents
        </button>
        <div className="admin-drawer-item admin-drawer-item-disabled">Services <span>soon</span></div>
        <div className="admin-drawer-item admin-drawer-item-disabled">Automations <span>soon</span></div>
        <div className="admin-drawer-divider" />
        <button type="button" className="admin-drawer-item" onClick={onSelectApiKeys}>
          API Keys
        </button>
      </div>
    </div>
  );
}
