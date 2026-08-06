import { getTroubleshootingTip } from "./errorTips";
import { AlertIcon } from "./icons";

interface ErrorBannerProps {
  message: string;
  deviceType?: string;
}

export function ErrorBanner({ message, deviceType }: ErrorBannerProps) {
  const tip = getTroubleshootingTip(message, deviceType);

  return (
    <div className="error-banner">
      <AlertIcon className="error-banner-icon" />
      <div>
        <div className="error-banner-message">{message}</div>
        {tip && <div className="error-banner-tip">{tip}</div>}
      </div>
    </div>
  );
}
