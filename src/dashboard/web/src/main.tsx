import React from 'react';
import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { createRoot } from 'react-dom/client';
import App from './App';
import '../styles/beautifului.css';
import '../styles/app.css';

createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);
