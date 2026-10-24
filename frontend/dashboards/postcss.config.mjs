// Tailwind utilities are emitted UNLAYERED and every selector is scoped under .bk-scope (a class on the app root / each chart island), so:
//  - they never restyle the rest of the PEMS page (Bootstrap shares class names like p-4, mb-3, text-center),
//  - inside the island they out-rank Bootstrap's reboot (class + utility beats a single class/element) without !important.
export default {
  plugins: {
    "@tailwindcss/postcss": {},
    "postcss-prefix-selector": {
      prefix: ".bk-scope",
      transform(prefix, selector, prefixed) {
        if (selector === ":root" || selector === "html" || selector === "body") return prefix;
        if (selector.startsWith(":root ")) return prefix + selector.slice(5);
        if (selector.startsWith(".bk-scope")) return selector;
        return prefixed;
      },
    },
  },
};
