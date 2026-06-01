import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Translate, {translate} from '@docusaurus/Translate';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

export default function Home(): ReactNode {
  const links = [
    {
      to: '/docs/getting-started/quickstart',
      title: translate({
        id: 'homepage.link.quickstart.title',
        message: 'Quickstart',
        description: 'Homepage quickstart link title',
      }),
      text: translate({
        id: 'homepage.link.quickstart.text',
        message: 'Place the controller prefab, select Luma4, and send your first TSMP frame.',
        description: 'Homepage quickstart link description',
      }),
    },
    {
      to: '/docs/components/setup',
      title: translate({
        id: 'homepage.link.setup.title',
        message: 'Setup',
        description: 'Homepage setup link title',
      }),
      text: translate({
        id: 'homepage.link.setup.text',
        message: 'Refresh codecs, apply bindings, and keep the Encoder and Decoder configured.',
        description: 'Homepage setup link description',
      }),
    },
    {
      to: '/docs/developer',
      title: translate({
        id: 'homepage.link.developer.title',
        message: 'Developer Docs',
        description: 'Homepage developer docs link title',
      }),
      text: translate({
        id: 'homepage.link.developer.text',
        message: 'Build custom network behaviours, codecs, and shader decode paths.',
        description: 'Homepage developer docs link description',
      }),
    },
  ];

  return (
    <Layout
      title={translate({
        id: 'homepage.meta.title',
        message: 'Texture Stream Message Protocol',
        description: 'Homepage browser title',
      })}
      description={translate({
        id: 'homepage.meta.description',
        message: 'TSMP documentation for VRChat worlds and Unity packages.',
        description: 'Homepage meta description',
      })}>
      <main className={styles.main}>
        <section className={styles.hero}>
          <div className="container">
            <p className={styles.eyebrow}>
              <Translate id="homepage.eyebrow" description="Homepage small label">
                KIBA_Labs TSMP
              </Translate>
            </p>
            <Heading as="h1" className={styles.title}>
              <Translate id="homepage.title" description="Homepage main title">
                Texture Stream Message Protocol
              </Translate>
            </Heading>
            <p className={styles.subtitle}>
              <Translate id="homepage.subtitle" description="Homepage subtitle">
                Send structured runtime data through visible texture frames in VRChat worlds.
              </Translate>
            </p>
            <div className={styles.actions}>
              <Link className="button button--primary button--lg" to="/docs/intro">
                <Translate id="homepage.action.docs" description="Homepage docs button">
                  Read the docs
                </Translate>
              </Link>
              <Link className="button button--secondary button--lg" to="/docs/getting-started/quickstart">
                <Translate id="homepage.action.start" description="Homepage start button">
                  Start setup
                </Translate>
              </Link>
            </div>
          </div>
        </section>

        <section className={styles.links}>
          <div className="container">
            <div className={styles.linkGrid}>
              {links.map((item) => (
                <Link className={styles.linkCard} to={item.to} key={item.to}>
                  <span className={styles.linkTitle}>{item.title}</span>
                  <span className={styles.linkText}>{item.text}</span>
                </Link>
              ))}
            </div>
          </div>
        </section>
      </main>
    </Layout>
  );
}
