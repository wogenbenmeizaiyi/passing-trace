"""Run a CI command and report its elapsed time without echoing arguments."""
import subprocess
import sys
from publish_android_release import stage

if __name__ == "__main__":
    with stage(sys.argv[1]):
        result = subprocess.run(sys.argv[2:], check=False)
        if result.returncode:
            # Preserve failure for job dependency gates; no exception/secret dump.
            print(f"::error::{sys.argv[1]}未通过 (exit {result.returncode})", flush=True)
            sys.exit(result.returncode)
