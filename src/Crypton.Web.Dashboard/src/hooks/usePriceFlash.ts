import { useState, useEffect, useRef } from 'react';

export type PriceDirection = 'up' | 'down' | 'unchanged';

export function usePriceFlash(direction: PriceDirection, duration = 200) {
  const [flashClass, setFlashClass] = useState('');
  const prevDirectionRef = useRef(direction);

  useEffect(() => {
    if (direction !== 'unchanged' && direction !== prevDirectionRef.current) {
      setFlashClass(direction === 'up' ? 'price-flash-up' : 'price-flash-down');
      
      const timer = setTimeout(() => {
        setFlashClass('');
      }, duration);

      prevDirectionRef.current = direction;
      
      return () => clearTimeout(timer);
    }
  }, [direction, duration]);

  return flashClass;
}

export function usePreviousPrice() {
  const previousRef = useRef<number | null>(null);

  return previousRef;
}
