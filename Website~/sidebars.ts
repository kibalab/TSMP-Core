import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docs: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      items: [
        'getting-started/installation',
        'getting-started/quickstart',
        'getting-started/scene-checklist',
        'getting-started/package-layout',
      ],
    },
    {
      type: 'category',
      label: 'Concepts',
      items: [
        'concepts/architecture',
        'concepts/protocol',
        'concepts/codec-system',
        'concepts/glossary',
      ],
    },
    {
      type: 'category',
      label: 'Components',
      items: [
        'components/setup',
        'components/encoder',
        'components/decoder',
        'components/network-behaviour',
        'components/sync-components',
        'components/debug-canvas',
      ],
    },
    {
      type: 'category',
      label: 'Codecs',
      items: [
        'codecs/overview',
      ],
    },
    {
      type: 'category',
      label: 'Guides',
      items: [
        'guides/texture-transport',
        'guides/obs-spout-loopback',
        'guides/performance-tuning',
      ],
    },
    {
      type: 'category',
      label: 'Developer',
      items: [
        'developer/index',
        {
          type: 'category',
          label: 'Custom Network Behaviour',
          items: [
            'developer/custom-network-behaviour',
            'developer/custom-network-variables',
            'developer/custom-network-rpc',
            'developer/custom-network-packed-data',
          ],
        },
        'developer/frame-algorithms',
        {
          type: 'category',
          label: 'Custom Codec',
          items: [
            'developer/custom-codec',
            'developer/codec-implementation',
            'developer/codec-shaders',
            'developer/codec-package-definition',
            'developer/codec-validation',
          ],
        },
        'developer/udonsharp-notes',
        'developer/testing',
      ],
    },
    {
      type: 'category',
      label: 'Scripting API',
      items: [
        'scripting-api/index',
        {
          type: 'category',
          label: 'Core Components',
          items: [
            'scripting-api/behaviour',
            'scripting-api/encoder',
            'scripting-api/decoder',
            'scripting-api/setup',
          ],
        },
        {
          type: 'category',
          label: 'Network API',
          items: [
            'scripting-api/network-behaviour',
            'scripting-api/transsync',
            'scripting-api/network-components',
          ],
        },
        {
          type: 'category',
          label: 'Codec API',
          items: [
            'scripting-api/codec',
            'scripting-api/codec-catalog',
            'scripting-api/decode-common',
            'scripting-api/decode-byte-output',
          ],
        },
        {
          type: 'category',
          label: 'Protocol API',
          items: [
            'scripting-api/protocol',
            'scripting-api/frame-header',
            'scripting-api/network-frame',
          ],
        },
      ],
    },
    'troubleshooting/index',
  ],
};

export default sidebars;
