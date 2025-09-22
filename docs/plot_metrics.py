#!/usr/bin/env python3
import os
import csv
import argparse
from typing import List, Dict

try:
    import matplotlib.pyplot as plt
except Exception as e:
    plt = None


def read_csv(path: str) -> List[Dict[str, str]]:
    rows: List[Dict[str, str]] = []
    if not os.path.isfile(path):
        return rows
    with open(path, 'r', newline='') as f:
        reader = csv.DictReader(f)
        for r in reader:
            rows.append(r)
    return rows


def plot(training_csv: str, episode_csv: str, out: str = None):
    if plt is None:
        print("matplotlib not available. Install via: pip install matplotlib")
        return

    train_rows = read_csv(training_csv)
    ep_rows = read_csv(episode_csv)

    fig, axes = plt.subplots(2, 2, figsize=(12, 8))

    # Training loss over steps
    if train_rows:
        steps = [int(r['step']) for r in train_rows]
        ema_loss = [float(r['ema_loss']) for r in train_rows]
        axes[0, 0].plot(steps, ema_loss, label='EMA Loss')
        axes[0, 0].set_title('Training Loss (EMA)')
        axes[0, 0].set_xlabel('Step')
        axes[0, 0].set_ylabel('Loss')
        axes[0, 0].grid(True)

        # Q stats
        qmean = [float(r['q_mean']) for r in train_rows]
        qmax = [float(r['q_max']) for r in train_rows]
        axes[0, 1].plot(steps, qmean, label='Q mean')
        axes[0, 1].plot(steps, qmax, label='Q max')
        axes[0, 1].set_title('Q-value stats')
        axes[0, 1].set_xlabel('Step')
        axes[0, 1].set_ylabel('Q')
        axes[0, 1].legend()
        axes[0, 1].grid(True)

    # Episode rewards over sessions
    if ep_rows:
        sessions = [int(r['total_session']) for r in ep_rows]
        r_seek = [float(r['acc_seeker_reward']) for r in ep_rows]
        r_hide = [float(r['acc_hider_reward']) for r in ep_rows]
        axes[1, 0].plot(sessions, r_seek, label='Seeker reward per episode')
        axes[1, 0].plot(sessions, r_hide, label='Hider reward per episode')
        axes[1, 0].set_title('Episode Rewards')
        axes[1, 0].set_xlabel('Total Session Index')
        axes[1, 0].set_ylabel('Accumulated Reward')
        axes[1, 0].legend()
        axes[1, 0].grid(True)

        # Visibility ratio per episode
        vis = [float(r['visibility_ratio']) for r in ep_rows]
        axes[1, 1].plot(sessions, vis, label='Visibility ratio')
        axes[1, 1].set_title('Episode Visibility Ratio')
        axes[1, 1].set_xlabel('Total Session Index')
        axes[1, 1].set_ylabel('Ratio')
        axes[1, 1].grid(True)

    plt.tight_layout()

    if out:
        os.makedirs(os.path.dirname(out) or '.', exist_ok=True)
        plt.savefig(out)
        print(f'Saved figure to {out}')
    else:
        plt.show()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Plot training and episode metrics from logs directory')
    parser.add_argument('--logs', default=os.path.join(os.path.dirname(__file__), '..', 'ToolUse.Sim', 'bin', 'Debug', 'net9.0', 'logs'), help='Path to logs directory (defaults to build output logs)')
    parser.add_argument('--out', default=None, help='Path to save plot (PNG). If not set, will show interactively.')
    args = parser.parse_args()

    logs_dir = os.path.abspath(args.logs)
    training_csv = os.path.join(logs_dir, 'training_metrics.csv')
    episode_csv = os.path.join(logs_dir, 'episode_metrics.csv')

    print('Using logs dir:', logs_dir)
    print('Training CSV:', training_csv)
    print('Episode CSV:', episode_csv)

    plot(training_csv, episode_csv, args.out)
