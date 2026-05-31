import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const siteUrl = process.env.DOCUSAURUS_URL ?? 'https://kibalabs.github.io';
const baseUrl = process.env.DOCUSAURUS_BASE_URL ?? '/tsmp/';
const repositoryUrl = process.env.DOCUSAURUS_REPOSITORY_URL ?? 'https://github.com/KIBA-Labs/TSMP';

const config: Config = {
  title: 'TSMP',
  tagline: 'Texture Stream Messaging Protocol for VRChat worlds',
  favicon: 'img/tsmp-logo.svg',

  url: siteUrl,
  baseUrl,
  organizationName: 'KIBA-Labs',
  projectName: 'tsmp',

  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'ko', 'ja'],
    localeConfigs: {
      en: {
        label: 'English',
      },
      ko: {
        label: '한국어',
      },
      ja: {
        label: '日本語',
      },
    },
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        language: ['en', 'ko', 'ja'],
        indexDocs: true,
        indexPages: true,
        indexBlog: false,
        docsRouteBasePath: 'docs',
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
        searchBarPosition: 'right',
        searchResultLimits: 10,
        searchResultContextMaxLength: 80,
      },
    ],
  ],

  themeConfig: {
    image: 'img/tsmp-social-card.svg',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'TSMP',
      logo: {
        alt: 'TSMP logo',
        src: 'img/tsmp-logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docs',
          position: 'left',
          label: 'Docs',
        },
        {
          href: repositoryUrl,
          label: 'GitHub',
          position: 'right',
        },
        {
          type: 'localeDropdown',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Packages',
          items: [
            {
              label: 'Core',
              to: '/docs/getting-started/package-layout',
            },
            {
              label: 'Codecs',
              to: '/docs/codecs/overview',
            },
          ],
        },
        {
          title: 'Guides',
          items: [
            {
              label: 'Quickstart',
              to: '/docs/getting-started/quickstart',
            },
            {
              label: 'Performance',
              to: '/docs/guides/performance-tuning',
            },
          ],
        },
        {
          title: 'Project',
          items: [
            {
              label: 'Troubleshooting',
              to: '/docs/troubleshooting',
            },
            {
              label: 'GitHub',
              href: repositoryUrl,
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} KIBA_Labs.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
