import { useState, useEffect, useRef, useCallback } from 'react';

interface UseNumberTransitionOptions {
  duration?: number;
  easing?: (t: number) => number;
}

export function useNumberTransition(
  targetValue: number,
  options: UseNumberTransitionOptions = {}
): number {
  const { duration = 150 } = options;
  
  const [displayValue, setDisplayValue] = useState(targetValue);
  const animationRef = useRef<number | null>(null);
  const startValueRef = useRef(targetValue);
  const startTimeRef = useRef<number>(0);

  const animate = useCallback((timestamp: number) => {
    if (!startTimeRef.current) {
      startTimeRef.current = timestamp;
    }

    const elapsed = timestamp - startTimeRef.current;
    const progress = Math.min(elapsed / duration, 1);

    const easeOutQuad = (t: number) => t * (2 - t);
    const easedProgress = easeOutQuad(progress);

    const current = startValueRef.current + (targetValue - startValueRef.current) * easedProgress;
    setDisplayValue(current);

    if (progress < 1) {
      animationRef.current = requestAnimationFrame(animate);
    }
  }, [targetValue, duration]);

  useEffect(() => {
    if (targetValue === displayValue) return;

    startValueRef.current = displayValue;
    startTimeRef.current = 0;

    animationRef.current = requestAnimationFrame(animate);

    return () => {
      if (animationRef.current) {
        cancelAnimationFrame(animationRef.current);
      }
    };
  }, [targetValue, animate, displayValue]);

  return displayValue;
}

export function AnimatedNumber({ 
  value, 
  format = (v: number) => v.toString(),
  duration = 150,
}: { 
  value: number; 
  format?: (v: number) => string;
  duration?: number;
}) {
  const displayValue = useNumberTransition(value, { duration });
  
  return <span>{format(Math.round(displayValue * 100) / 100)}</span>;
}
