export type MissingType = 'null' | 'undefined' | 'missing' | 'valid';

export function getMissingType(value: unknown): MissingType {
  if (value === null) return 'null';
  if (value === undefined) return 'undefined';
  return 'valid';
}

export function isMissing(value: unknown): boolean {
  return value === null || value === undefined;
}

export function getOrDefault<T>(
  value: T | null | undefined,
  defaultValue: T,
  allowEmptyString: boolean = false
): T {
  if (value === null || value === undefined) {
    return defaultValue;
  }
  if (!allowEmptyString && typeof value === 'string' && value === '') {
    return defaultValue;
  }
  return value;
}

export function displayValue<T>(
  value: T | null | undefined,
  formatter: (val: T) => string = String,
  placeholder: string = '—'
): string {
  if (isMissing(value)) {
    return placeholder;
  }
  if (typeof value === 'string' && value === '') {
    return placeholder;
  }
  try {
    return formatter(value as T);
  } catch {
    console.warn('Formatter error for value:', value);
    return placeholder;
  }
}

export function safeArray<T>(value: T | null | undefined): T[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value;
}

export interface NestedMissingReport {
  path: string;
  type: MissingType;
}

export function findMissingPaths(
  obj: Record<string, unknown>,
  prefix: string = ''
): NestedMissingReport[] {
  const results: NestedMissingReport[] = [];

  for (const [key, value] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${key}` : key;

    if (isMissing(value)) {
      results.push({
        path,
        type: value === null ? 'null' : 'undefined',
      });
    } else if (typeof value === 'object' && value !== null) {
      results.push(...findMissingPaths(value as Record<string, unknown>, path));
    }
  }

  return results;
}
