import type { Metadata } from "next";
import { CopilotKit } from "@copilotkit/react-core";
import "@copilotkit/react-ui/styles.css";
import "./globals.css";

export const metadata: Metadata = {
  title: "Flint Chart Agent — AI Data Visualization",
  description:
    "Create beautiful, interactive charts through natural language using the Flint Chart Agent powered by AGUI and CopilotKit.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <CopilotKit runtimeUrl="/api/copilotkit" agent="flint_agent">
          {children}
        </CopilotKit>
      </body>
    </html>
  );
}
