# One-off: extract HTML from Cursor transcript and apply hero/footer fixes.
from __future__ import annotations

import json
import re
import sys

TRANSCRIPT = r"C:\Users\KarthikKarri\.cursor\projects\d-Ai-LeadScoring-Agent\agent-transcripts\72bb17e6-d26e-400e-b06b-2df853008779\72bb17e6-d26e-400e-b06b-2df853008779.jsonl"
OUT = r"d:\Ai_LeadScoring_Agent-\api\LeadScoring.Api\EmailTemplatesReference\hiperbrains-email-full.html"

OLD_NON_MSO_HERO = r"""          <!--[if !mso]><!-->
         <img 
  src="https://leadscoring.hiperbrains.com/assets/images/hero-banner.jpg"
  alt="HiperBrains"
  width="160"
  height="33"
  class="logo-img"
  style="display:block;width:160px;height:auto;max-width:160px;"
>
          <!--<![endif]-->"""

NEW_NON_MSO_HERO = r"""          <!--[if !mso]><!-->
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;">
            <tr>
              <td align="center" style="padding:0;">
                <div class="hero-outer" style="border-radius:16px;overflow:hidden;max-width:568px;margin:0 auto;">
                  <img
                    src="https://leadscoring.hiperbrains.com/assets/images/hero-banner.jpg"
                    alt="Is your hiring process efficient? HiperBrains hiring automation."
                    width="568"
                    class="hero-img fluid"
                    style="display:block;width:100%;max-width:568px;height:auto;border-radius:16px;">
                </div>
              </td>
            </tr>
          </table>
          <!--<![endif]-->"""


def extract_html(path: str) -> str | None:
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            if obj.get("role") != "user":
                continue
            for part in obj.get("message", {}).get("content", []):
                if isinstance(part, dict) and part.get("type") == "text":
                    t = part.get("text", "")
                    i = t.find("<!DOCTYPE html>")
                    if i >= 0:
                        return t[i:]
    return None


def main():
    html = extract_html(TRANSCRIPT)
    if not html:
        print("NO HTML FOUND", file=sys.stderr)
        sys.exit(1)

    if OLD_NON_MSO_HERO in html:
        html = html.replace(OLD_NON_MSO_HERO, NEW_NON_MSO_HERO, 1)
        print("Replaced hero block (exact match)")
    else:
        hero_rx = re.compile(
            r"<!--\[if !mso\]><!-->\s*<img[^>]*hero-banner\.jpg[^>]*>\s*<!--<!\[endif\]-->",
            re.DOTALL | re.IGNORECASE,
        )
        m = hero_rx.search(html)
        if m:
            html = html[: m.start()] + NEW_NON_MSO_HERO + html[m.end() :]
            print("Replaced hero block (regex)")
        else:
            print("WARN: hero block not replaced", file=sys.stderr)

    footer_pat = re.compile(
        r'<img src="data:image/png;base64,[^"]+" alt="HiperBrains" width="130"',
        re.IGNORECASE,
    )
    html2, n = footer_pat.subn(
        '<img src="https://leadscoring.hiperbrains.com/assets/images/logo.png" alt="HiperBrains" width="130"',
        html,
        count=1,
    )
    html = html2
    if n:
        print("Footer logo -> HTTPS URL")

    BASE = "https://leadscoring.hiperbrains.com/assets/images/"
    for alt_text, fname in (
        ("Instagram", "instagram.png"),
        ("Facebook", "facebook.png"),
        ("X", "x.png"),
        ("YouTube", "youtube.png"),
    ):
        url = BASE + fname
        pat = rf'<img src="data:image/png;base64,[^"]+" alt="{re.escape(alt_text)}"'
        html, c = re.subn(
            pat,
            f'<img src="{url}" alt="{alt_text}"',
            html,
            count=1,
            flags=re.IGNORECASE,
        )
        if c:
            print(f"Social {alt_text} -> URL")

    with open(OUT, "w", encoding="utf-8") as wf:
        wf.write(html)
    print("Wrote", OUT, "bytes", len(html.encode("utf-8")))


if __name__ == "__main__":
    main()
