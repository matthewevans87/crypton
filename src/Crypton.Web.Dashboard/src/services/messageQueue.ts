interface QueuedMessage<T = unknown> {
  id: string;
  type: string;
  data: T;
  timestamp: number;
}

const MAX_QUEUE_SIZE = 100;

class MessageQueue {
  private queue: QueuedMessage[] = [];
  private isProcessing = false;
  private replayCallbacks: ((message: QueuedMessage) => void)[] = [];

  enqueue<T>(type: string, data: T): void {
    const message: QueuedMessage<T> = {
      id: `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
      type,
      data,
      timestamp: Date.now(),
    };

    this.queue.push(message);

    if (this.queue.length > MAX_QUEUE_SIZE) {
      this.queue.shift();
    }
  }

  replay(callback: (message: QueuedMessage) => void): void {
    this.replayCallbacks.push(callback);
    this.processQueue();
  }

  private async processQueue(): Promise<void> {
    if (this.isProcessing || this.queue.length === 0) {
      return;
    }

    this.isProcessing = true;

    const messages = [...this.queue];
    this.queue = [];

    for (const message of messages) {
      for (const callback of this.replayCallbacks) {
        try {
          callback(message);
        } catch (error) {
          console.error('Error processing queued message:', error);
        }
      }
    }

    this.isProcessing = false;
  }

  clear(): void {
    this.queue = [];
  }

  size(): number {
    return this.queue.length;
  }

  onReconnect(callback: (message: QueuedMessage) => void): () => void {
    this.replayCallbacks.push(callback);
    return () => {
      const index = this.replayCallbacks.indexOf(callback);
      if (index > -1) {
        this.replayCallbacks.splice(index, 1);
      }
    };
  }
}

export const messageQueue = new MessageQueue();
