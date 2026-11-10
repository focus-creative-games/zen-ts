"""Install a prepared native SDK into a Unity project's Library; package stays read-only."""
import argparse
from pathlib import Path
from native_sdk import install,validate,project_root
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--sdk',type=Path,required=True);p.add_argument('--project',type=Path);p.add_argument('--validate-only',action='store_true');a=p.parse_args()
 if a.validate_only:print('Validated',validate(a.sdk,ROOT)['target'])
 else:print('Installed SDK:',install(a.sdk,project_root(a.project),ROOT));print('Unity will generate Editor plugins and metadata under Assets/ZenTSGenerated/Native.')
if __name__=='__main__':main()
