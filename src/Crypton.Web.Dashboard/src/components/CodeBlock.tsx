import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism';

interface CodeBlockProps {
  code: string;
  language?: 'json' | 'javascript' | 'typescript' | 'text';
  showLineNumbers?: boolean;
  maxHeight?: string;
}

const customStyle: React.CSSProperties = {
  margin: 0,
  padding: 'var(--space-2)',
  borderRadius: '4px',
  fontSize: '11px',
  fontFamily: 'var(--font-mono)',
  backgroundColor: 'var(--bg-viewport)',
};

export function CodeBlock({ code, language = 'text', showLineNumbers = false, maxHeight = '300px' }: CodeBlockProps) {
  const detectedLanguage = detectLanguage(code, language);
  
  const displayCode = detectedLanguage === 'json' ? formatJson(code) : code;

  return (
    <div style={{ 
      maxHeight, 
      overflow: 'auto',
      backgroundColor: 'var(--bg-viewport)',
      borderRadius: '4px',
    }}>
      <SyntaxHighlighter
        language={detectedLanguage}
        style={vscDarkPlus}
        showLineNumbers={showLineNumbers}
        customStyle={customStyle}
        wrapLines={true}
        lineNumberStyle={{
          minWidth: '2em',
          paddingRight: '1em',
          color: 'var(--text-tertiary)',
          userSelect: 'none',
        }}
      >
        {displayCode}
      </SyntaxHighlighter>
    </div>
  );
}

function detectLanguage(code: string, defaultLanguage: string): string {
  if (defaultLanguage !== 'text') {
    return defaultLanguage;
  }
  
  try {
    JSON.parse(code);
    return 'json';
  } catch {
    return 'text';
  }
}

function formatJson(code: string): string {
  try {
    const parsed = JSON.parse(code);
    return JSON.stringify(parsed, null, 2);
  } catch {
    return code;
  }
}

interface JsonViewerProps {
  data: unknown;
  maxHeight?: string;
}

export function JsonViewer({ data, maxHeight = '300px' }: JsonViewerProps) {
  const jsonString = JSON.stringify(data, null, 2);
  
  return (
    <CodeBlock 
      code={jsonString} 
      language="json" 
      maxHeight={maxHeight}
    />
  );
}
