import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import SignupForm from './SignupForm';

export default function Signup() {
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  const handleSignup = async (
    _name: string,
    _email: string,
    _password: string,
    _confirmPassword: string
  ) => {
    try {
      setError(null);

      // Auth0 está lidando com registro/login, então não chamamos register aqui
      navigate('/dashboard');
    } catch (err) {
      setError('An unexpected error occurred. Please try again.');
      console.error('Signup error:', err);
    }
  };

  return <SignupForm onSubmit={handleSignup} error={error} />;
}
