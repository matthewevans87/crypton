const locale = typeof navigator !== 'undefined' ? navigator.language : 'en-US';

export type TimestampFormat = 'realtime' | 'history' | 'relative' | 'full';

const formats: Record<TimestampFormat, Intl.DateTimeFormatOptions> = {
  realtime: {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  },
  history: {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  },
  full: {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  },
  relative: {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  },
};

export function formatTimestamp(
  isoString: string | Date | null | undefined,
  format: TimestampFormat = 'history',
  fallback: string = '—'
): string {
  if (!isoString) {
    return fallback;
  }

  try {
    const date = isoString instanceof Date ? isoString : new Date(isoString);

    if (isNaN(date.getTime())) {
      console.warn('Invalid timestamp:', isoString);
      return fallback;
    }

    if (format === 'relative') {
      return formatRelative(date);
    }

    return new Intl.DateTimeFormat(locale, formats[format]).format(date);
  } catch (error) {
    console.warn('Timestamp format error:', error);
    return fallback;
  }
}

export function formatTime(date: Date | string | null | undefined): string {
  return formatTimestamp(date ?? null, 'realtime');
}

export function formatDateTime(date: Date | string | null | undefined): string {
  return formatTimestamp(date ?? null, 'history');
}

export function formatRelative(
  date: Date | string | null | undefined,
  fallback: string = '—'
): string {
  if (!date) return fallback;

  const now = typeof date === 'string' ? new Date(date) : date;
  const nowDate = new Date();

  const diffMs = nowDate.getTime() - now.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  if (diffSec < 0) {
    return formatTimestamp(date, 'history');
  }

  if (diffSec < 60) {
    return 'just now';
  }

  if (diffMin < 60) {
    return `${diffMin}m ago`;
  }

  if (diffHour < 24) {
    return `${diffHour}h ago`;
  }

  if (diffDay < 7) {
    return `${diffDay}d ago`;
  }

  return formatTimestamp(date, 'history');
}

export function toLocalTime(isoString: string | null | undefined): Date | null {
  if (!isoString) return null;
  const date = new Date(isoString);
  return isNaN(date.getTime()) ? null : date;
}

export function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return `${seconds}s`;
  }
  if (seconds < 3600) {
    const min = Math.floor(seconds / 60);
    const sec = seconds % 60;
    return sec > 0 ? `${min}h ${sec}s` : `${min}m`;
  }
  const hours = Math.floor(seconds / 3600);
  const min = Math.floor((seconds % 3600) / 60);
  return min > 0 ? `${hours}h ${min}m` : `${hours}h`;
}

export function formatPositionTime(seconds: number): string {
  if (seconds < 3600) {
    const min = Math.floor(seconds / 60);
    const sec = seconds % 60;
    return `${min}m ${sec}s`;
  }
  const hours = Math.floor(seconds / 3600);
  const min = Math.floor((seconds % 3600) / 60);
  return min > 0 ? `${hours}d ${min}h` : `${hours}d`;
}
