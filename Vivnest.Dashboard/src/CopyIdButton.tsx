import { useState } from "react";
import { CheckIcon, CopyIcon } from "./icons";

interface CopyIdButtonProps {
  value: string;
}

// Icon-only, not the raw id as flowing text - a GUID wraps badly in any
// fixed-width layout (header line or metric tile, both tried and both
// looked bad). The full value is still reachable via the title tooltip and
// a click-to-copy, without ever rendering the whole string on the page.
export function CopyIdButton({ value }: CopyIdButtonProps) {
  const [copied, setCopied] = useState(false);

  async function handleClick() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard API can fail (permissions, insecure context) - a failed
      // copy isn't worth surfacing an error for.
    }
  }

  return (
    <button
      type="button"
      className="id-copy-button"
      title={value}
      onClick={handleClick}
      aria-label={copied ? "Copied" : `Copy ${value}`}
    >
      {copied ? <CheckIcon className="id-copy-icon" /> : <CopyIcon className="id-copy-icon" />}
    </button>
  );
}
