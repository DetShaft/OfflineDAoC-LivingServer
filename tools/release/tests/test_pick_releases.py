import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pick_releases as pr  # noqa: E402


def rel(tag, draft=False, package=True):
    assets = [{"name": f"OfflineDAoC-LivingServer-{tag[1:]}.zip"}] if package else []
    return {"tag_name": tag, "draft": draft, "assets": assets}


# As the fork's releases stood on 2026-11-11, newest first by date (v1.1.10 was published after v1.1.9).
FORK = [rel("v1.1.10-beta"), rel("v1.1.9-beta"), rel("v1.1.8-beta"), rel("v1.1.7-beta"),
        rel("v1.1.6-beta"), rel("v1.1.5-beta"), rel("v1.1.4-beta")]


class PickReleases(unittest.TestCase):
    def test_newest_by_version_not_by_name(self):
        # "v1.1.10" sorts before "v1.1.9" as text; it is the newer version.
        shuffled = [FORK[1], FORK[0]] + FORK[2:]
        self.assertEqual(pr.pick(shuffled), {"package": "v1.1.10-beta", "from": "v1.1.9-beta", "to": "v1.1.10-beta"})

    def test_given_package_updates_from_the_one_before_it(self):
        self.assertEqual(pr.pick(FORK, "v1.1.7-beta"), {"package": "v1.1.7-beta", "from": "v1.1.6-beta", "to": "v1.1.7-beta"})

    def test_drafts_and_releases_without_their_zip_are_skipped(self):
        releases = [rel("v1.1.11-beta", draft=True), rel("v1.1.10-beta"), rel("v1.1.9-beta", package=False)] + FORK[2:]
        self.assertEqual(pr.pick(releases), {"package": "v1.1.10-beta", "from": "v1.1.8-beta", "to": "v1.1.10-beta"})

    def test_tags_that_are_not_releases_are_never_picked(self):
        # v1.1-beta and v1.1.1-beta are only tags in the fork: they are not in the release list, so never "from".
        self.assertEqual(pr.pick(FORK, "v1.1.5-beta")["from"], "v1.1.4-beta")
        with self.assertRaises(pr.Stop):
            pr.pick(FORK, "v1.1.1-beta")

    def test_oldest_release_has_nothing_to_update_from(self):
        with self.assertRaises(pr.Stop):
            pr.pick(FORK, "v1.1.4-beta")

    def test_no_releases(self):
        with self.assertRaises(pr.Stop):
            pr.pick([rel("v0.36-se.1"), rel("v1.2-beta", draft=True)])


if __name__ == "__main__":
    unittest.main()
