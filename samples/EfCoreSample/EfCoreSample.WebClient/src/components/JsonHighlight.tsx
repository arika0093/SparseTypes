import { Alert, Box, Code, Stack } from "@mantine/core";
import { useEffect, useState } from "react";
import { codeToHtml } from "shiki";

interface JsonHighlightProps {
  code: string;
}

export default function JsonHighlight({ code }: JsonHighlightProps) {
  const [html, setHtml] = useState<string>();
  const [error, setError] = useState<string>();

  useEffect(() => {
    let active = true;
    setHtml(undefined);
    setError(undefined);
    void codeToHtml(code, { lang: "json", theme: "dark-plus" })
      .then((highlighted) => {
        if (active) setHtml(highlighted);
      })
      .catch((cause: unknown) => {
        if (active)
          setError(cause instanceof Error ? cause.message : String(cause));
      });

    return () => {
      active = false;
    };
  }, [code]);

  return (
    <Stack gap="xs">
      {error && <Alert color="red">JSON highlighting failed: {error}</Alert>}
      <Box h={400} style={{ overflow: "auto" }}>
        {html ? (
          <Box
            className="payload-highlight"
            role="region"
            aria-label="ChangePayload JSON"
            dangerouslySetInnerHTML={{ __html: html }}
          />
        ) : (
          <Code block style={{ fontSize: "0.95rem", lineHeight: 1.6 }}>
            {code}
          </Code>
        )}
      </Box>
    </Stack>
  );
}
