import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const highlights = [
  {
    title: 'VRChat-first runtime',
    text: 'Encode UdonSharp state into a visible texture stream and decode it back into TSMP network behaviours.',
  },
  {
    title: 'Luma4 default path',
    text: 'Start with the included Luma4 codec and verify the texture transport before changing advanced settings.',
  },
  {
    title: 'Operational tooling',
    text: 'Setup, codec discovery, debug canvas, and troubleshooting docs are part of the workflow.',
  },
];

export default function Home(): ReactNode {
  return (
    <Layout
      title="Texture Stream Messaging Protocol"
      description="TSMP documentation for VRChat worlds and Unity packages">
      <main>
        <section className={styles.hero}>
          <div className="container">
            <p className={styles.eyebrow}>KIBA_Labs TSMP</p>
            <Heading as="h1" className={styles.title}>
              Texture Stream Messaging Protocol for VRChat worlds
            </Heading>
            <p className={styles.subtitle}>
              TSMP transports structured runtime data through rendered texture frames, with
              UdonSharp-compatible encoder, decoder, network behaviours, and a default Luma4 texture path.
            </p>
            <div className={styles.actions}>
              <Link className="button button--primary button--lg" to="/docs/intro">
                Read the docs
              </Link>
              <Link className="button button--secondary button--lg" to="/docs/getting-started/quickstart">
                Quickstart
              </Link>
            </div>
          </div>
        </section>

        <section className={styles.highlights}>
          <div className="container">
            <div className={styles.grid}>
              {highlights.map((item) => (
                <article className={styles.card} key={item.title}>
                  <h2>{item.title}</h2>
                  <p>{item.text}</p>
                </article>
              ))}
            </div>
          </div>
        </section>
      </main>
    </Layout>
  );
}
